using Flow.Documents;

namespace Flow.Security;

public interface IDocumentCanonicalizer
{
    public string CanonicalizationVersion { get; }

    public byte[] Canonicalize(FlowDocument document);
}
