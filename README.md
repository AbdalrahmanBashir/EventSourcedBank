# EventSourcedBank

A comprehensive event-sourced banking application built with .NET 8, demonstrating CQRS (Command Query Responsibility Segregation) and Event Sourcing patterns. The application provides a complete banking system with account management, transaction processing, and real-time read models.

## Architecture Overview

This application follows a clean architecture pattern with clear separation of concerns:

- **EventSourcedBank.Domain**: Core business logic and domain models
- **EventSourcedBank.Infrastructure**: Data persistence, event store, and read model projections
- **EventSourcedBank.Api**: REST API controllers and DTOs

### Event Sourcing & CQRS

The application implements:
- **Event Sourcing**: All state changes are captured as immutable events
- **CQRS**: Separate command and query models for optimal performance
- **Event Store**: PostgreSQL-based event persistence with optimistic concurrency control
- **Read Models**: Projected views optimized for queries and reporting

## Features

### Core Banking Operations
- **Account Management**: Open, close, freeze, and unfreeze accounts
- **Transaction Processing**: Deposits, withdrawals, and fee applications
- **Overdraft Management**: Configurable overdraft limits with usage tracking
- **Account Holder Management**: Name changes and account modifications

### Advanced Features
- **Real-time Projections**: Background service maintains read models
- **Concurrency Control**: Optimistic locking prevents data conflicts
- **Multi-currency Support**: Handle different currencies per account
- **Comprehensive Reporting**: Account summaries, overdrawn accounts, and analytics

## Technology Stack

- **.NET 8**: Modern C# with latest language features
- **PostgreSQL**: Dual database setup Event Store + Read Model
- **Dapper**: Lightweight ORM for data access
- **Docker**: Containerized deployment with Docker Compose
- **Swagger/OpenAPI**: Interactive API documentation

## Prerequisites

- .NET 8 SDK
- Docker and Docker Compose
- PostgreSQL if running locally without Docker

## Quick Start

### Using Docker Compose (Recommended)

1. **Clone the repository**
   ```bash
   git clone https://github.com/AbdalrahmanBashir/EventSourcedBank.git
   cd EventSourcedBank
   ```

2. **Start the application**
   ```bash
   docker-compose up --build
   ```

3. **Access the application**
   - API: https://localhost:5001
   - Swagger UI: https://localhost:5001/swagger
   - Event Store DB: localhost:5555
   - Read Model DB: localhost:5554

### Manual Setup

1. **Create databases**
   ```sql
   -- Event Store Database
   CREATE DATABASE bank_events;
   
   -- Read Model Database  
   CREATE DATABASE bank_readmodel;
   ```

2. **Update connection strings** in `src/EventSourcedBank.Api/appsettings.json`

3. **Run the application**
   ```bash
   dotnet run --project src/EventSourcedBank.Api
   ```

## API Documentation

### Command Endpoints Write Model

#### Account Management
- `POST /api/accounts` - Open a new account
- `GET /api/accounts/{id}` - Get account details
- `POST /api/accounts/{id}/deposit` - Deposit money
- `POST /api/accounts/{id}/withdraw` - Withdraw money
- `POST /api/accounts/{id}/freeze` - Freeze account
- `POST /api/accounts/{id}/unfreeze` - Unfreeze account
- `POST /api/accounts/{id}/close` - Close account

#### Account Modifications
- `PATCH /api/accounts/{id}/overdraft-limit` - Change overdraft limit
- `PATCH /api/accounts/{id}/holder-name` - Change account holder name
- `POST /api/accounts/{id}/fees` - Apply fees

### Query Endpoints Read Model

#### Account Queries
- `GET /api/read/accounts/{id}` - Get account balance details
- `GET /api/read/accounts` - List all accounts with filtering
- `GET /api/read/accounts/overdrawn` - Get overdrawn accounts
- `GET /api/read/accounts/summary` - Get account summary statistics

### Request/Response Examples

#### Open Account
```http
POST /api/accounts
Content-Type: application/json

{
  "holderName": "John Doe",
  "overdraftLimit": 1000.00,
  "currency": "USD",
  "initialBalance": 500.00
}
```

#### Deposit Money
```http
POST /api/accounts/{id}/deposit
Content-Type: application/json

{
  "amount": 250.00,
  "currency": "USD"
}
```

#### List Accounts with Filters
```http
GET /api/read/accounts?status=Open&currency=USD&minBalance=100&sortBy=balance&sortDir=desc&limit=20&offset=0
```

## Domain Model

### Bank Account Aggregate

The `BankAccountAggregate` is the core domain entity that encapsulates all business rules:

#### Account States
- **New**: Initial state before opening
- **Open**: Active account for transactions
- **Frozen**: Temporarily suspended (deposits allowed)
- **Closed**: Permanently closed account

#### Business Rules
- Accounts must have positive initial balance
- Withdrawals respect overdraft limits
- Frozen accounts block withdrawals but allow deposits
- Closed accounts must have zero balance
- Currency consistency is enforced across operations

### Domain Events

All state changes are captured as immutable events:

- `BankAccountOpened` - Account creation
- `MoneyDeposited` - Deposit transaction
- `MoneyWithdrawn` - Withdrawal transaction
- `AccountFrozen` - Account suspension
- `AccountUnfrozen` - Account reactivation
- `AccountClosed` - Account closure
- `OverdraftLimitChanged` - Limit modification
- `AccountHolderNameChanged` - Name update
- `FeeApplied` - Fee deduction

## Data Architecture

### Event Store Schema
```sql
event_store.events (
    event_id UUID PRIMARY KEY,
    stream_id UUID NOT NULL,
    version INT NOT NULL,
    event_type TEXT NOT NULL,
    event_data JSONB NOT NULL,
    metadata JSONB NOT NULL,
    occurred_on TIMESTAMPTZ NOT NULL,
    global_position BIGSERIAL
)
```

### Read Model Schema
```sql
readmodel.account_balance (
    account_id UUID PRIMARY KEY,
    holder_name TEXT NOT NULL,
    status TEXT NOT NULL,
    balance_amount NUMERIC(18,2) NOT NULL,
    balance_currency TEXT NOT NULL,
    overdraft_limit NUMERIC(18,2) NOT NULL,
    available_to_withdraw NUMERIC(18,2) NOT NULL,
    version INT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
)
```

## Event Processing

### Event Store
- **Optimistic Concurrency**: Version-based conflict detection
- **Atomic Transactions**: All events in a batch succeed or fail together
- **Global Ordering**: Events are processed in chronological order
- **JSON Serialization**: Flexible event data storage

### Read Model Projection
- **Background Service**: Continuous event processing
- **Checkpoint Management**: Tracks processing position
- **Batch Processing**: Efficient bulk updates
- **Idempotent Operations**: Safe to replay events

## Testing the Application

### Sample API Calls

1. **Create an account**
   ```bash
   curl -X POST "https://localhost:5001/api/accounts" \
        -H "Content-Type: application/json" \
        -d '{
          "holderName": "Alice Smith",
          "overdraftLimit": 500.00,
          "currency": "USD",
          "initialBalance": 1000.00
        }'
   ```

2. **Make a deposit**
   ```bash
   curl -X POST "https://localhost:5001/api/accounts/{account-id}/deposit" \
        -H "Content-Type: application/json" \
        -d '{
          "amount": 250.00,
          "currency": "USD"
        }'
   ```

3. **Query account details**
   ```bash
   curl -X GET "https://localhost:5001/api/read/accounts/{account-id}"
   ```

## Configuration

### Environment Variables
- `ConnectionStrings__EventStore`: Event store database connection
- `ConnectionStrings__ReadModel`: Read model database connection
- `ASPNETCORE_ENVIRONMENT`: Application environment (Development/Production)

### Docker Configuration
The application uses Docker Compose with:
- **API Service**: Ports 5000 (HTTP) and 5001 (HTTPS)
- **Event Store DB**: Port 5555
- **Read Model DB**: Port 5554
- **Persistent Volumes**: Data persistence across container restarts


## Deployment

### Production Considerations
- **Database Security**: Use managed PostgreSQL services
- **Connection Pooling**: Configure appropriate pool sizes
- **Monitoring**: Implement application performance monitoring
- **Backup Strategy**: Regular event store and read model backups
- **Scaling**: Consider read model replication for high availability

### Performance Optimization
- **Read Model Indexing**: Optimized for common query patterns
- **Batch Processing**: Efficient event projection
- **Connection Management**: Proper resource disposal
- **Caching**: Consider read model caching for frequently accessed data

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Submit a pull request

## License

This project is licensed under the MIT License.

## Support

For questions or issues:
1. Check the [Issues](https://github.com/your-repo/issues) page
2. Review the API documentation at `/swagger`
3. Examine the event store for debugging

---

**Note**: This application is designed for educational and demonstration purposes. For production banking systems, additional security, compliance, and regulatory features would be required.