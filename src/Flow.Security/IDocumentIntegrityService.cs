using Flow.Documents;

namespace Flow.Security;

public interface IDocumentIntegrityService
{
    public DocumentHash ComputeHash(FlowDocument document);
}
