using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using System.Text;

namespace RabbitMQConsumerWorker
{
    public class Worker(ILogger<Worker> logger) : BackgroundService
    {
        private ConnectionMultiplexer _redisConnection;
        private IDatabase _redisDb;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            try
            {

                _redisConnection = await ConnectionMultiplexer.ConnectAsync("localhost");
                _redisDb = _redisConnection.GetDatabase(0);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Redis bağlantı hatası!");
                return;
            }

            var factory = new ConnectionFactory() { HostName = "localhost" };


            IConnection connection = null;
            IChannel channel = null;

            try
            {
                connection = await factory.CreateConnectionAsync();
                channel = await connection.CreateChannelAsync();



                await channel.ExchangeDeclareAsync(exchange: RabbitMqConfig.DL_EXCHANGE, type: "fanout", durable: true);
                await channel.QueueDeclareAsync(queue: RabbitMqConfig.DL_QUEUE, durable: true, exclusive: false, autoDelete: false, arguments: null);
                await channel.QueueBindAsync(queue: RabbitMqConfig.DL_QUEUE, exchange: RabbitMqConfig.DL_EXCHANGE, routingKey: "");
                Console.WriteLine($" [OK] Dead Letter Kuyruk/Exchange ({RabbitMqConfig.DL_EXCHANGE}) ASENKRON kuruldu.");

                await channel.ExchangeDeclareAsync(exchange: RabbitMqConfig.MAIN_EXCHANGE, type: "direct", durable: true);
                var anaKuyrukArguments = new Dictionary<string, object?> { { "x-dead-letter-exchange", RabbitMqConfig.DL_EXCHANGE } };
                await channel.QueueDeclareAsync(queue: RabbitMqConfig.MAIN_QUEUE, durable: true, exclusive: false, autoDelete: false, arguments: anaKuyrukArguments);
                await channel.QueueBindAsync(queue: RabbitMqConfig.MAIN_QUEUE, exchange: RabbitMqConfig.MAIN_EXCHANGE, routingKey: "message.key");
                Console.WriteLine($" [OK] Ana Kuyruk ({RabbitMqConfig.MAIN_QUEUE}) DLX ile ASENKRON kuruldu.");


                // --- 1. ANA KUYRUK T�KET�C�S� (HASH KULLANIMI) ---
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (sender, eventArgs) =>
                {
                    var message = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
                    var deliveryTagString = eventArgs.DeliveryTag.ToString();
                    Console.WriteLine($"\n [C] Mesaj alındı: {message} (DeliveryTag: {deliveryTagString})");

                    var redisValue = $"{DateTimeOffset.UtcNow:o} - {message}";

                    try
                    {
                        if (message.Contains("hata", StringComparison.CurrentCultureIgnoreCase))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($" [!!!] Mesaj işlenemedi. DLX'e YöNLENDiRiLiYOR.");
                            await channel.BasicRejectAsync(deliveryTag: eventArgs.DeliveryTag, requeue: false);
                            Console.ForegroundColor = ConsoleColor.White;
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($" [OK] Mesaj başarıyla işlendi ve onaylandı (ACK).");

                            var result = await _redisDb.HashSetAsync("messages:success", deliveryTagString, redisValue);
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($" [REDIS] messages:success hash'ine yazıldı. DeliveryTag: {deliveryTagString}, Yeni alan: {result}");
                            await channel.BasicAckAsync(deliveryTag: eventArgs.DeliveryTag, multiple: false);
                            Console.ForegroundColor = ConsoleColor.White;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.WriteLine($" [!!!] İstisna oluştu: {ex.Message}. Reddediliyor ve DL listesine ekleniyor.");

                        await channel.BasicRejectAsync(deliveryTag: eventArgs.DeliveryTag, requeue: false);
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                };
                await channel.BasicConsumeAsync(RabbitMqConfig.MAIN_QUEUE, autoAck: false, consumer);


                // --- 2. DEAD LETTER KUYRUK (DLQ) T�KET�C�S� (HASH KULLANIMI) ---
                var dlqConsumer = new AsyncEventingBasicConsumer(channel);

                dlqConsumer.ReceivedAsync += async (sender, eventArgs) =>
                {
                    var message = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
                    var deliveryTagString = eventArgs.DeliveryTag.ToString();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n [DLQ] Dead Letter Queue'dan alındı: {message} (DeliveryTag: {deliveryTagString})");

                    var dlqReviewValue = $"{DateTimeOffset.UtcNow:o} - DLQ REVIEWED: {message}";


                    var result = await _redisDb.HashSetAsync("dlq:reviewed_messages", deliveryTagString, dlqReviewValue);
                    Console.WriteLine($" [REDIS] dlq:reviewed_messages hash'ine yazıldı. DeliveryTag: {deliveryTagString}, Yeni alan: {result}");

                    await channel.BasicAckAsync(deliveryTag: eventArgs.DeliveryTag, multiple: false);
                    Console.ForegroundColor = ConsoleColor.White;
                };

                await channel.BasicConsumeAsync(RabbitMqConfig.DL_QUEUE, autoAck: false, dlqConsumer);
                Console.WriteLine($" [OK] Dead Letter Kuyruğu ({RabbitMqConfig.DL_QUEUE}) dinlenmeye başlandı.");


                Console.WriteLine("Consumer: Tüketici başltıldı.");

                // Worker'�n aktif kalmas�n� sa�layan d�ng�
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Genel Worker Hatası!");
            }
            finally
            {
                // Temizlik i�lemleri (Kalan k�s�mlar ayn�)
                if (channel is { IsOpen: true }) await channel.CloseAsync(200, "Worker stopped");
                if (connection is { IsOpen: true }) await connection.CloseAsync(200, "Worker stopped");
                if (_redisConnection is { IsConnected: true }) _redisConnection.Dispose();
            }
        }
    }
}