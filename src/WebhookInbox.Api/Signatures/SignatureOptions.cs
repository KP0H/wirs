namespace WebhookInbox.Api.Signatures;

public class SignatureOptions
{
    public List<SourceSignatureConfig> Sources { get; set; } = new();
}