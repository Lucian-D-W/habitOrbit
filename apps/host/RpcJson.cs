using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pulse.Core;

namespace Pulse.Host;

internal static class RpcJson
{
    // Keep 64-bit facts exact across JavaScript's narrower integer range.
    public static readonly JsonSerializerOptions Options=new(Rules.Json){NumberHandling=JsonNumberHandling.AllowReadingFromString,Converters={new Int64Text()}};
    private sealed class Int64Text:JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options) => reader.TokenType==JsonTokenType.String?long.Parse(reader.GetString()!,CultureInfo.InvariantCulture):reader.GetInt64();
        public override void Write(Utf8JsonWriter writer,long value,JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
    public static JsonElement Property(JsonElement p,string name) => p.TryGetProperty(name,out var value)?value:p.GetProperty(JsonNamingPolicy.SnakeCaseLower.ConvertName(name));
    public static Guid Id(JsonElement p,string name="activityId") => Property(p,name).GetGuid();
    public static DateOnly Day(JsonElement p,string name) => DateOnly.ParseExact(p.GetProperty(name).GetString()!,"yyyy-MM-dd",CultureInfo.InvariantCulture);
}
