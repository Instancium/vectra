namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

public interface ITextExtractor
{
    Task<IReadOnlyList<PageText>> ExtractAsync(
        TextExtractionRequest request,
        CancellationToken cancellationToken = default);
}
