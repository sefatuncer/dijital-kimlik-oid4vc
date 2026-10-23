using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;

namespace DijitalKimlik.Crypto.SelectiveDisclosure;

/// <summary>
/// RFC 9901 §7.1, 3–5. adımlar. Nesnelerde <c>_sd</c> dizisindeki, dizilerde <c>{"...": özet}</c>
/// biçimindeki özetler disclosure'larla eşlenir. Yerleştirilen değerin içi de aynı kurala göre
/// işlenir (iç içe disclosure). Eşleşmeyen özet sahte (decoy) ya da gösterilmemiş alandır ve yok sayılır.
/// </summary>
internal static class DisclosureProcessor
{
    public static JsonObject Process(JsonObject payload, IReadOnlyList<Disclosure> disclosures)
    {
        var algorithm = JoseJson.GetString(payload, "_sd_alg") ?? SdJwt.HashAlgorithm;
        if (algorithm != SdJwt.HashAlgorithm)
        {
            throw new CryptoValidationException("unsupported_sd_alg", $"Desteklenmeyen _sd_alg: {algorithm}.");
        }

        var state = new State(disclosures);
        var result = (JsonObject)payload.DeepClone();
        result.Remove("_sd_alg");
        ProcessObject(result, state);

        // Hiçbir özetin karşılığı olmayan disclosure, imzalı içeriğe sonradan eklenmiş demektir.
        if (state.Used.Count != state.ByDigest.Count)
        {
            throw new CryptoValidationException("unreferenced_disclosure", "İmzalı içerikte karşılığı olmayan bir disclosure var.");
        }

        return result;
    }

    private static void ProcessObject(JsonObject obj, State state)
    {
        if (obj.TryGetPropertyValue("_sd", out var sdNode))
        {
            if (sdNode is not JsonArray digests)
            {
                throw new CryptoValidationException("invalid_sd", "_sd bir dizi olmalı.");
            }

            obj.Remove("_sd");
            foreach (var item in digests)
            {
                var digest = AsDigest(item);
                if (!state.Take(digest, out var disclosure))
                {
                    continue;
                }

                if (disclosure.Name is null)
                {
                    throw new CryptoValidationException("invalid_disclosure", "Nesne alanı için 3 elemanlı disclosure bekleniyordu.");
                }

                if (obj.ContainsKey(disclosure.Name))
                {
                    throw new CryptoValidationException("claim_name_conflict", $"'{disclosure.Name}' alanı içerikte zaten var.");
                }

                obj[disclosure.Name] = disclosure.Value?.DeepClone();
            }
        }

        foreach (var name in obj.Select(p => p.Key).ToList())
        {
            ProcessChild(obj[name], state);
        }
    }

    private static void ProcessArray(JsonArray array, State state)
    {
        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (array[i] is not JsonObject { Count: 1 } element || !element.TryGetPropertyValue("...", out var digestNode))
            {
                continue;
            }

            if (!state.Take(AsDigest(digestNode), out var disclosure))
            {
                // Gösterilmemiş dizi elemanı sonuçtan çıkarılır.
                array.RemoveAt(i);
                continue;
            }

            if (disclosure.Name is not null)
            {
                throw new CryptoValidationException("invalid_disclosure", "Dizi elemanı için 2 elemanlı disclosure bekleniyordu.");
            }

            array[i] = disclosure.Value?.DeepClone();
        }

        foreach (var item in array)
        {
            ProcessChild(item, state);
        }
    }

    private static void ProcessChild(JsonNode? node, State state)
    {
        switch (node)
        {
            case JsonObject child:
                ProcessObject(child, state);
                break;
            case JsonArray child:
                ProcessArray(child, state);
                break;
        }
    }

    private static string AsDigest(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? digest)
            ? digest
            : throw new CryptoValidationException("invalid_sd", "Özet metin olmalı.");

    private sealed class State
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public State(IReadOnlyList<Disclosure> disclosures)
        {
            foreach (var disclosure in disclosures)
            {
                if (!ByDigest.TryAdd(disclosure.Digest, disclosure))
                {
                    throw new CryptoValidationException("duplicate_disclosure", "Aynı disclosure iki kez gönderilmiş.");
                }
            }
        }

        public Dictionary<string, Disclosure> ByDigest { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Used { get; } = new(StringComparer.Ordinal);

        /// <summary>Özeti kaydeder; disclosure'ı varsa döndürür. Aynı özet iki yerde geçerse reddeder.</summary>
        public bool Take(string digest, [NotNullWhen(true)] out Disclosure? disclosure)
        {
            if (!_seen.Add(digest))
            {
                throw new CryptoValidationException("duplicate_digest", "Aynı özet içerikte iki kez geçiyor.");
            }

            if (!ByDigest.TryGetValue(digest, out disclosure))
            {
                return false;
            }

            Used.Add(digest);
            return true;
        }
    }
}
