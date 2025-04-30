using MongoDB.Bson;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Serializers;

namespace Astro.Utils;

public class UlongKeyDictionarySerializer : DictionarySerializerBase<Dictionary<ulong, ulong>, ulong, ulong>
{
    public UlongKeyDictionarySerializer()
        : base(DictionaryRepresentation.Document, new UInt64Serializer(BsonType.String), new UInt64Serializer(BsonType.Int64)) { }

    protected override ICollection<KeyValuePair<ulong, ulong>> CreateAccumulator()
    {
        return new Dictionary<ulong, ulong>();
    }
}