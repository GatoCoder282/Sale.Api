using MySql.Data.MySqlClient;
using Sale.Domain.Ports;
using Sale.Infraestructure.Data; 
using System;
using System.Threading.Tasks;

namespace Sale.Infraestructure.Persistence
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly MySqlConnection _connection;
        private MySqlTransaction? _transaction;

        private SaleRepository? _saleRepository;
        private OutboxRepository? _outboxRepository;

        public UnitOfWork()
        {
            _connection = DatabaseConnection.Instance.GetConnection();

        }

        public ISaleRepository SaleRepository
        {
            get
            {
                return _saleRepository ??= new SaleRepository(_connection, _transaction);
            }
        }

        public IOutboxRepository OutboxRepository
        {
            get
            {
                return _outboxRepository ??= new OutboxRepository(_connection, _transaction);
            }
        }

        public async Task BeginTransactionAsync()
        {
            if (_connection.State != System.Data.ConnectionState.Open)
                await _connection.OpenAsync();

            _transaction = await _connection.BeginTransactionAsync();

            _saleRepository = null;
            _outboxRepository = null;
        }

        public async Task EnsureConnectionOpenAsync()
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }
        }

        public async Task CommitAsync()
        {
            try
            {
                await _transaction!.CommitAsync();
            }
            catch
            {
                await RollbackAsync();
                throw;
            }
            finally
            {
                await DisposeTransaction();
            }
        }

        public async Task RollbackAsync()
        {
            if (_transaction != null)
                await _transaction.RollbackAsync();

            await DisposeTransaction();
        }

        private async Task DisposeTransaction()
        {
            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            _transaction?.Dispose();
            _connection?.Dispose();
        }
    }
}