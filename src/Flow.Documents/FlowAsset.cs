using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

public sealed record FlowAsset
{
    public FlowAsset(AssetId id, string mediaType, string fileName, ReadOnlyMemory<byte> data)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Id = id;
        MediaType = mediaType;
        FileName = fileName;
        Data = ImmutableArray.CreateRange(data.ToArray());
    }

    public AssetId Id { get; }

    public string MediaType { get; }

    public string FileName { get; }

    public ImmutableArray<byte> Data { get; }
}
