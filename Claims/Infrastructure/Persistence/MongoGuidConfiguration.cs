using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Claims.Infrastructure.Persistence;

/// <summary>Stores every Guid as a standard BSON UUID (binary subtype 4), readable by any MongoDB client.</summary>
public static class MongoGuidConfiguration
{
    private static readonly Lock Gate = new();
    private static bool _applied;

    /// <summary>Safe to call many times; the serializer registry only accepts the registration once per process.</summary>
    public static void Apply()
    {
        lock (Gate)
        {
            if (_applied) return;
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            _applied = true;
        }
    }
}