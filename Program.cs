using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Xml.Linq;
namespace tcpClient11._04
{
    internal class Program
    {
        private static readonly int MaxRequestsPerSession = 5;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(1);
        private static readonly int MaxConnections = 3; 
        private static readonly Dictionary<string, (int requestCount, DateTime lastRequestTime)> clientRequestData = new();
        private static int currentConnections = 0; 

        static async Task Main(string[] args)
        {
            serverRun();
            await clientRun();
        }

        static async Task serverRun()
        {
            var tcpListener = new TcpListener(IPAddress.Any, 8888);
            try
            {
                tcpListener.Start();
                Console.WriteLine("Сервер запущен>>>");

                while (true)
                {

                    if (currentConnections >= MaxConnections)
                    {
                        Console.WriteLine("Максимальна кількість підключень досягнута. Чекайте на звільнення місця.");
                        var tcpClient = await tcpListener.AcceptTcpClientAsync();
                        await RejectClient(tcpClient, "Сервер під максимальним навантаженням, спробуйте під'єднатися пізніше.");
                    }
                    else
                    {
                        var tcpClient = await tcpListener.AcceptTcpClientAsync();
                        Interlocked.Increment(ref currentConnections); 
                        Task.Run(async () => await ProccessClient(tcpClient));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                tcpListener.Stop();
            }
        }

        static async Task ProccessClient(TcpClient tcpClient)
        {
            var exchange = new Dictionary<string, int>()
            {
                {"USD", 40},
                {"EURO", 43},
                {"GBP", 54},
                {"MXN", 2 }
            };

            var clientEndPoint = tcpClient.Client.RemoteEndPoint.ToString(); 
            var connectionTime = DateTime.Now;
            Console.WriteLine($"Підключено: {clientEndPoint}, Час підключення: {connectionTime}");


            if (IsRateLimited(clientEndPoint))
            {
                Console.WriteLine("Перевищено ліміт запитів. Сесію відхилено.");
                await RejectClient(tcpClient, "Перевищено ліміт запитів. Сесія буде доступна через 1 хвилину.");
                return;
            }


            UpdateRequestData(clientEndPoint);


            while (true)
            {
                var stream = tcpClient.GetStream();
                var response = new List<byte>();
                int bytesRead;

                while (true)
                {
                    while ((bytesRead = stream.ReadByte()) != '\n')
                    {
                        if (bytesRead == -1) return; 
                        response.Add((byte)bytesRead);
                    }

                    var word = Encoding.UTF8.GetString(response.ToArray());

                    if (word == "END")
                        break;

                    if (!exchange.TryGetValue(word.Trim(), out var num)) 
                    {
                        var translation = "не найдено в словаре";
                        translation += '\n';
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(translation));
                    }
                    else
                    {
                        var translation = $"Курс {word}: {num}";
                        translation += '\n';
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(translation));
                    }

                    response.Clear();
                }
            }


            Interlocked.Decrement(ref currentConnections);
            Console.WriteLine($"Підключення закрите: {tcpClient.Client.RemoteEndPoint}");
        }

        static bool IsRateLimited(string clientEndPoint)
        {

            if (clientRequestData.TryGetValue(clientEndPoint, out var data))
            {
                if (DateTime.Now - data.lastRequestTime < RequestTimeout)
                {

                    return data.requestCount >= MaxRequestsPerSession;
                }
            }
            return false;
        }

        static void UpdateRequestData(string clientEndPoint)
        {
            if (clientRequestData.ContainsKey(clientEndPoint))
            {
                clientRequestData[clientEndPoint] = (clientRequestData[clientEndPoint].requestCount + 1, DateTime.Now);
            }
            else
            {
                clientRequestData[clientEndPoint] = (1, DateTime.Now);
            }
        }

        static async Task RejectClient(TcpClient tcpClient, string message)
        {
            var stream = tcpClient.GetStream();
            byte[] data = Encoding.UTF8.GetBytes(message + '\n');
            await stream.WriteAsync(data);
            tcpClient.Close(); 
        }

        static async Task clientRun()
        {
            using TcpClient tcpClient = new TcpClient();
            await tcpClient.ConnectAsync("127.0.0.1", 8888);

            var words = new string[]
            {
                "USD", "EURO", "MXN1-" 
            };

            var stream = tcpClient.GetStream();
            int bytesRead;

            foreach (var word in words)
            {
                byte[] data = Encoding.UTF8.GetBytes(word + '\n');
                await stream.WriteAsync(data); 

                var response = new List<byte>(); 

                
                while ((bytesRead = stream.ReadByte()) != '\n')
                {
                    if (bytesRead == -1) break;  
                    response.Add((byte)bytesRead);
                }

                var tran = Encoding.UTF8.GetString(response.ToArray());
                Console.WriteLine($"{word} - {tran}");

                response.Clear(); 
            }

            await stream.WriteAsync(Encoding.UTF8.GetBytes("END\n"));  
            Console.WriteLine("Все сообщения отправлены");
        }
    }
}
