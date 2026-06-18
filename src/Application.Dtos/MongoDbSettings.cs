using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Dtos
{
    public class MongoDbSettings
    {
        public string ConnectionString { get; set; } = "mongodb://localhost:27017";
        public string DatabaseName { get; set; } = "OrderSagaDb";
        public string SagaCollection { get; set; } = "OrderSagas";
        public string OutboxCollection { get; set; } = "OutboxMessages";
    }
}
