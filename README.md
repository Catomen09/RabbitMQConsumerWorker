# RabbitMQ Consumer Worker

A .NET 8.0 Worker Service that consumes messages from RabbitMQ queues, processes them, and stores the results in Redis. The service implements Dead Letter Exchange (DLX) pattern for handling failed messages.

## Features

- **RabbitMQ Message Consumption**: Consumes messages from a main queue using async event handlers
- **Dead Letter Exchange (DLX)**: Automatically routes failed messages to a Dead Letter Queue (DLQ)
- **Redis Integration**: Stores processed messages in Redis using hash data structures
- **Error Handling**: Comprehensive error handling with automatic DLQ routing for exceptions
- **Async/Await Pattern**: Fully asynchronous message processing
- **Logging**: Detailed console logging with color-coded messages

## Architecture

The service implements a two-queue pattern:

1. **Main Queue** (`message.queue`): Processes incoming messages
   - Successfully processed messages are stored in Redis hash `messages:success`
   - Messages containing "hata" (error) are rejected and routed to DLQ
   - Exceptions during processing also route messages to DLQ

2. **Dead Letter Queue** (`deadletter.queue`): Handles failed messages
   - Stores reviewed messages in Redis hash `dlq:reviewed_messages`
   - Allows for later analysis and reprocessing

## Prerequisites

- .NET 8.0 SDK or later
- RabbitMQ Server (running on localhost:5672)
- Redis Server (running on localhost:6379)

### Using Docker

You can run RabbitMQ and Redis using Docker:

```bash
# Start RabbitMQ
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management

# Start Redis with Redis Insight
docker run -d --name redis-stack -p 6379:6379 -p 8001:8001 redis/redis-stack:latest
```

Access RabbitMQ Management UI: http://localhost:15672 (guest/guest)
Access Redis Insight: http://localhost:8001

## Configuration

The service uses the following configuration (defined in `RabbitMqConfig.cs`):

- **Host**: `localhost`
- **Main Exchange**: `main.message.exchange` (direct type)
- **DL Exchange**: `error.dl.exchange` (fanout type)
- **Main Queue**: `message.queue`
- **DL Queue**: `deadletter.queue`
- **Routing Key**: `message.key`
- **Redis Database**: 0

## Redis Data Structure

Messages are stored in Redis using hash data structures:

### Successful Messages
- **Hash Key**: `messages:success`
- **Field**: DeliveryTag (string)
- **Value**: Timestamp (ISO 8601) + message content

Example:
```
HGET messages:success "1"
"2025-12-14T12:23:00.0802737+00:00 - Hello World"
```

### Dead Letter Queue Messages
- **Hash Key**: `dlq:reviewed_messages`
- **Field**: DeliveryTag (string)
- **Value**: Timestamp + "DLQ REVIEWED: " + message content

## Message Processing Flow

1. Message arrives in the main queue
2. Consumer receives the message
3. Processing logic:
   - **Success**: Message stored in `messages:success` hash, ACK sent
   - **Contains "hata"**: Message rejected, routed to DLQ
   - **Exception**: Message rejected, routed to DLQ
4. DLQ consumer processes failed messages
5. DLQ messages stored in `dlq:reviewed_messages` hash

## Building and Running

### Build

```bash
dotnet build
```

### Run

```bash
dotnet run
```

Or run from Visual Studio/VS Code.

## Testing

### Publish a Test Message

Using RabbitMQ Management UI:
1. Go to http://localhost:15672
2. Navigate to Exchanges → `main.message.exchange`
3. Publish a message with routing key `message.key`

Using RabbitMQ CLI:
```bash
docker exec rabbitmq rabbitmqadmin publish exchange=main.message.exchange routing_key=message.key payload="Test message"
```

### Verify in Redis

Using Redis CLI:
```bash
docker exec redis-stack redis-cli

# View successful messages
HGETALL messages:success

# View DLQ messages
HGETALL dlq:reviewed_messages
```

Using Redis Insight:
1. Open http://localhost:8001
2. Navigate to Browser
3. Search for `messages:success` or `dlq:reviewed_messages`

## Project Structure

```
RabbitMQConsumerWorker/
├── Worker.cs              # Main worker service implementation
├── Program.cs             # Application entry point
├── RabbitMqConfig.cs      # RabbitMQ configuration constants
└── RabbitMQConsumerWorker.csproj  # Project file
```

## Dependencies

- **Microsoft.Extensions.Hosting** (8.0.1): Worker service hosting
- **RabbitMQ.Client** (7.2.0): RabbitMQ client library
- **StackExchange.Redis** (2.10.1): Redis client library

## Logging

The service provides color-coded console output:

- 🟢 **Green**: Successfully processed messages
- 🔴 **Red**: Failed messages (routed to DLQ)
- 🟡 **Yellow**: DLQ messages
- 🔵 **Cyan**: Redis operations
- ⚪ **White**: General information

## Error Handling

- Redis connection errors: Logged and worker stops gracefully
- RabbitMQ connection errors: Logged and worker stops gracefully
- Message processing exceptions: Caught, logged, and message routed to DLQ
- Connection cleanup: Properly disposes of connections in finally block

## Future Enhancements

- Configuration via appsettings.json
- Retry mechanism with exponential backoff
- Metrics and health checks
- Support for multiple message types
- Message serialization/deserialization (JSON, Protobuf)

## License

This project is provided as-is for educational and development purposes.

## Contributing

Contributions, issues, and feature requests are welcome!

