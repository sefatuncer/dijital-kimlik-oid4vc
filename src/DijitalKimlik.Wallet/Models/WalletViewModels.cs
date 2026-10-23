using DijitalKimlik.Wallet.Credentials;
using DijitalKimlik.Wallet.Presentations;

namespace DijitalKimlik.Wallet.Models;

public sealed record ReceiveViewModel(string OfferJson, OfferPreview Preview, string? Error);

public sealed record ConsentViewModel(PresentationRequest Request, string RawQuery, IReadOnlyList<StoredCredential> Matches);
