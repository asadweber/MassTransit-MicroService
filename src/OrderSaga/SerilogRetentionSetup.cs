using MongoDB.Bson;
using MongoDB.Driver;

public static class SerilogRetentionSetup
{
    private const string TtlIndexName = "TTL_LogCollection_2Days";


    /// <summary>
    /// Ensures that a TTL index exists on the "UtcTimeStamp" field of the specified MongoDB collection for Serilog logs. 
    /// If the index already exists but with a different expiration time, it will be dropped and recreated with the new TTL.
    /// </summary>
    /// <param name="config">The application's configuration.</param>
    /// <param name="retentionDays">The number of days to retain logs.</param>
    /// <exception cref="InvalidOperationException">Thrown if the MongoDB configuration is missing or invalid.</exception>
    public static void EnsureSerilogTtlIndex(IConfiguration config, int retentionDays = 1)
    {
        var databaseUrl = config["Serilog:WriteTo:0:Args:databaseUrl"];
        var collectionName = config["Serilog:WriteTo:0:Args:collectionName"];

        if (string.IsNullOrWhiteSpace(databaseUrl) || string.IsNullOrWhiteSpace(collectionName))
            throw new InvalidOperationException("Serilog MongoDB sink config (databaseUrl/collectionName) not found.");

        var mongoUrl = MongoUrl.Create(databaseUrl);
        var client = new MongoClient(mongoUrl);
        var database = client.GetDatabase(mongoUrl.DatabaseName);
        var collection = database.GetCollection<BsonDocument>(collectionName);

        // MongoDBBson sink stores the event timestamp in the "UtcTimeStamp" field as a real BSON Date
        var indexKeys = Builders<BsonDocument>.IndexKeys.Ascending("UtcTimeStamp");
        var indexOptions = new CreateIndexOptions
        {
            ExpireAfter = TimeSpan.FromMinutes(retentionDays),
            Name = TtlIndexName,
            Background=true           
        };

        try
        {
            collection.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(indexKeys, indexOptions));
        }
        catch (MongoCommandException ex) when (ex.CodeName == "IndexOptionsConflict" || ex.Message.Contains("already exists"))
        {
            // Retention period changed — drop old index and recreate with new TTL
            collection.Indexes.DropOne(TtlIndexName);
            collection.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(indexKeys, indexOptions));
        }
    }
}