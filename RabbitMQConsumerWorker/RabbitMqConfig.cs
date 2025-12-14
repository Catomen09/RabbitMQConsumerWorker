namespace RabbitMQConsumerWorker
{
    public static class RabbitMqConfig
    {
        public const string HOST_NAME = "localhost";
        public const string MAIN_EXCHANGE = "main.message.exchange";
        public const string DL_EXCHANGE = "error.dl.exchange"; // Dead Letter Exchange
        public const string MAIN_QUEUE = "message.queue";
        public const string DL_QUEUE = "deadletter.queue";
    }
}