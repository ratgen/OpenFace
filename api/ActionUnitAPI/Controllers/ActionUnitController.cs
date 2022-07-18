using Microsoft.AspNetCore.Mvc;

using OpenCVWrappers;
using CppInterop.LandmarkDetector;
using FaceAnalyser_Interop;
using GazeAnalyser_Interop;
using FaceDetectorInterop;
using UtilitiesOF;
using System.Net.WebSockets;
using System.Text;

namespace AuApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ActionUnitController : ControllerBase
    {
        // For tracking
        FaceDetector face_detector;
        FaceModelParameters face_model_params;
        CLNF landmark_detector;

        // Allows for going forward in time step by step
        // Useful for visualising things
        private volatile int skip_frames = 0;

        int image_output_size = 112;
        public bool MaskAligned { get; set; } = true; // Should the aligned images be masked


        // For face analysis
        FaceAnalyserManaged face_analyser;
        GazeAnalyserManaged gaze_analyser;

        public bool RecordAligned { get; set; } = false; // Aligned face images
        public bool RecordHOG { get; set; } = false; // HOG features extracted from face images
        public bool Record2DLandmarks { get; set; } = false; // 2D locations of facial landmarks (in pixels)
        public bool Record3DLandmarks { get; set; } = false; // 3D locations of facial landmarks (in pixels)
        public bool RecordModelParameters { get; set; } = false; // Facial shape parameters (rigid and non-rigid geometry)
        public bool RecordPose { get; set; } = true; // Head pose (position and orientation)
        public bool RecordAUs { get; set; } = true; // Facial action units
        public bool RecordGaze { get; set; } = false; // Eye gaze
        public bool RecordTracked { get; set; } = false; // Recording tracked videos or images


        private volatile bool thread_running;
        private volatile bool thread_paused = false;


        // Selecting which landmark detector will be used

        public bool LandmarkDetectorCLM { get; set; } = false;
        public bool LandmarkDetectorCLNF { get; set; } = false;
        public bool LandmarkDetectorCECLM { get; set; } = true;


        // Selecting which face detector will be used
        public bool DetectorHaar { get; set; } = false;
        public bool DetectorHOG { get; set; } = false;
        public bool DetectorCNN { get; set; } = true;

        // For AU prediction, if videos are long dynamic models should be used
        public bool DynamicAUModels { get; set; } = true;


        [HttpGet("/ws")]
        public async Task Get()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                await Echo(webSocket);
            }
            else
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }

        [HttpPost(Name = "PostVideo")]
        public int PostVideo()
        {
            return 1;
        }

        private async Task Echo(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 1024];
            var receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), CancellationToken.None);


            byte[] container;
            while (!receiveResult.CloseStatus.HasValue)
            {
               await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer, 0, receiveResult.Count),
                    receiveResult.MessageType,
                    receiveResult.EndOfMessage,
                    CancellationToken.None);

                // write the stream to a file.
                var fileName = "/tmp/" + Guid.NewGuid() + "video.webm";
                using (var stream = new FileStream(fileName, FileMode.Append))
                    try
                    {
                        await stream.WriteAsync(buffer, 0, receiveResult.Count);
                    }
                    catch (Exception ex)
                    {}
                
                
                //Read video and process it
                SequenceReader reader = new SequenceReader(fileName, false);
                Thread processing_thread = new Thread(() => ProcessSequence(reader));
                processing_thread.Name = "Video processing";
                processing_thread.Start();
                

                /*using (var stream = new MemoryStream(buffer))
                    try
                    {
                       
                        await stream.WriteAsync(buffer, 0, receiveResult.Count);

                        Console.WriteLine(
                           "Capacity = {0}, Length = {1}, Position = {2}\n",
                           stream.Capacity.ToString(),
                           stream.Length.ToString(),
                           stream.Position.ToString());

                        stream.Seek(0, SeekOrigin.Begin);

                        container = new byte[receiveResult.Count];
                        stream.Read(container, 0, receiveResult.Count)
                    }
                    catch (Exception ex)
                    {}
                */

                receiveResult = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), CancellationToken.None);
                Console.WriteLine("Message type: " + receiveResult.MessageType);
                Console.WriteLine("Byte counts: " + receiveResult.Count);
                Console.WriteLine("End of message " + receiveResult.EndOfMessage);
             }

            await webSocket.CloseAsync(
                receiveResult.CloseStatus.Value,
                receiveResult.CloseStatusDescription,
                CancellationToken.None);
        }

        private void ProcessSequence(SequenceReader reader)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;

            thread_running = true;

            // Reload the face landmark detector if needed
            ReloadLandmarkDetector();

            if (!landmark_detector.isLoaded())
            {
                Console.WriteLine("landmark detector not found", Console.Error);
                thread_running = false;
                return;
            }

            // Set the face detector
            face_model_params.SetFaceDetector(DetectorHaar, DetectorHOG, DetectorCNN);
            face_model_params.optimiseForVideo();

            // Initialize the face analyser
            face_analyser = new FaceAnalyserManaged(AppDomain.CurrentDomain.BaseDirectory, DynamicAUModels, image_output_size, MaskAligned);

            // Reset the tracker
            landmark_detector.Reset();

            // Loading an image file
            var frame = reader.GetNextImage();
            var gray_frame = reader.GetCurrentFrameGray();

            // For FPS tracking
           // DateTime? startTime = CurrentTime;
           // var lastFrameTime = CurrentTime;

            // Empty image would indicate that the stream is over
            while (!gray_frame.IsEmpty)
            {

                if (!thread_running)
                {
                    break;
                }

                double progress = reader.GetProgress();

                bool detection_succeeding = landmark_detector.DetectLandmarksInVideo(frame, face_model_params, gray_frame);

                // The face analysis step (for AUs and eye gaze)
                face_analyser.AddNextFrame(frame, landmark_detector.CalculateAllLandmarks(), detection_succeeding, false);

                gaze_analyser.AddNextFrame(landmark_detector, detection_succeeding, reader.GetFx(), reader.GetFy(), reader.GetCx(), reader.GetCy());

                // Only the final face will contain the details
               // VisualizeFeatures(frame, visualizer_of, landmark_detector.CalculateAllLandmarks(), landmark_detector.GetVisibilities(), detection_succeeding, true, false, reader.GetFx(), reader.GetFy(), reader.GetCx(), reader.GetCy(), progress);

                // Record an observation
               // RecordObservation(recorder, visualizer_of.GetVisImage(), 0, detection_succeeding, reader.GetFx(), reader.GetFy(), reader.GetCx(), reader.GetCy(), reader.GetTimestamp(), reader.GetFrameNumber());

                while (thread_running & thread_paused && skip_frames == 0)
                {
                    Thread.Sleep(10);
                }

                if (skip_frames > 0)
                    skip_frames--;

                frame = reader.GetNextImage();
                gray_frame = reader.GetCurrentFrameGray();

                //lastFrameTime = CurrentTime;
                //processing_fps.AddFrame();
            }

            // Close the open video/webcam
            reader.Close();
        }

        private void ReloadLandmarkDetector()
        {
            bool reload = false;
            if (face_model_params.IsCECLM() && !LandmarkDetectorCECLM)
            {
                reload = true;
            }
            else if (face_model_params.IsCLNF() && !LandmarkDetectorCLNF)
            {
                reload = true;
            }
            else if (face_model_params.IsCLM() && !LandmarkDetectorCLM)
            {
                reload = true;
            }

            if (reload)
            {
                String root = AppDomain.CurrentDomain.BaseDirectory;

                face_model_params = new FaceModelParameters(root, LandmarkDetectorCECLM, LandmarkDetectorCLNF, LandmarkDetectorCLM);
                landmark_detector = new CLNF(face_model_params);
            }
        }


    }
}
