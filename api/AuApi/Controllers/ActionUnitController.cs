using Microsoft.AspNetCore.Mvc;

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

        private static async Task Echo(WebSocket webSocket)
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

                using(var stream = new FileStream("/tmp/file.webm", FileMode.Append))
                    try
                    {
                        await stream.WriteAsync(buffer, 0, receiveResult.Count);
                    }
                    catch (Exception ex)
                    {

                    }

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

        
    }
}
