using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Ludus.Domain.Verification;

namespace Ludus.Infrastructure.Verification;

public abstract record DatRecord;
public sealed record DatHeader(string? Name, string? Description, string? Version, string? Author) : DatRecord;
public sealed record DatGameRecord(DatGame Game) : DatRecord;

public sealed class LogiqxDatReader
{
    public const long MaximumBytes = 128 * 1024 * 1024;

    public async IAsyncEnumerable<DatRecord> ReadAsync(Stream content,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = XmlReader.Create(content, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, CloseInput = false,
            MaxCharactersInDocument = MaximumBytes, MaxCharactersFromEntities = 1024, IgnoreComments = true
        });
        await reader.MoveToContentAsync();
        if (reader.LocalName != "datafile" || reader.NodeType != XmlNodeType.Element || reader.IsEmptyElement)
            throw new FormatException("Expected a non-empty Logiqx datafile element.");
        var gameNames = new HashSet<string>(StringComparer.Ordinal);
        var seenHeader = false;
        var romCount = 0;
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1) continue;
            if (reader.LocalName is not ("header" or "game" or "machine"))
                throw new FormatException("Unsupported top-level DAT element.");
            using var subtree = reader.ReadSubtree();
            var element = await XElement.LoadAsync(subtree, LoadOptions.None, cancellationToken);
            if (element.Name.LocalName == "header")
            {
                if (seenHeader || gameNames.Count != 0) throw new FormatException("DAT header must occur once, before games.");
                seenHeader = true;
                yield return new DatHeader(Text(element, "name"), Text(element, "description"), Text(element, "version"), Text(element, "author"));
                continue;
            }

            var name = RequiredAttribute(element, "name");
            if (!gameNames.Add(name)) throw new FormatException("Duplicate game name in DAT.");
            if (gameNames.Count > 250000) throw new FormatException("DAT exceeds the game limit.");
            if (element.Elements().Any(x => x.Name.LocalName == "disk"))
                throw new FormatException("DAT disk hashes refer to decoded disk content, not ROM file bytes; disk entries are not supported.");
            var game = new DatGame { Name = name, Description = Text(element, "description"), Serial = Text(element, "serial"), Category = Text(element, "category") };
            var romNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var romElement in element.Elements().Where(x => x.Name.LocalName == "rom"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var romName = RequiredAttribute(romElement, "name");
                if (!romNames.Add(romName)) throw new FormatException("Duplicate ROM name in DAT game.");
                if (++romCount > 1000000) throw new FormatException("DAT exceeds the ROM limit.");
                var status = ((string?)romElement.Attribute("status") ?? "good").ToLowerInvariant();
                if (status is not ("good" or "verified" or "baddump" or "nodump")) throw new FormatException("Invalid DAT dump status.");
                var sizeText = (string?)romElement.Attribute("size");
                long? size = null;
                if (sizeText is not null)
                {
                    if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                        throw new FormatException("Invalid ROM size in DAT.");
                    size = parsed;
                }
                var rom = new DatRom
                {
                    Name = romName, Size = size, Status = status, Merge = (string?)romElement.Attribute("merge"),
                    Crc32 = Hash(romElement, "crc", 8), Md5 = Hash(romElement, "md5", 32), Sha1 = Hash(romElement, "sha1", 40)
                };
                if (status != "nodump" && rom.Crc32 is null && rom.Md5 is null && rom.Sha1 is null)
                    throw new FormatException("A dumped ROM must supply at least one checksum.");
                game.Roms.Add(rom);
            }
            yield return new DatGameRecord(game);
        }
        if (gameNames.Count == 0 || romCount == 0) throw new FormatException("DAT contains no ROM records.");
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        var value = ((string?)element.Attribute(name))?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 4096) throw new FormatException("Missing or oversized DAT name.");
        return value;
    }

    private static string? Text(XElement element, string name) => element.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim();

    private static string? Hash(XElement element, string attribute, int length)
    {
        var value = ((string?)element.Attribute(attribute))?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length != length || !value.All(Uri.IsHexDigit)) throw new FormatException("Invalid DAT checksum.");
        return value.ToLowerInvariant();
    }
}
