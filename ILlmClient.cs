using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CadDetailerAi
{
    public interface ILlmClient
    {
        Task<DraftResponse> CreateDraftAsync(
            string request,
            string notes,
            string drawingJson,
            CancellationToken cancellationToken);
    }
}
