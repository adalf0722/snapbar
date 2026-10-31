using System.Text;
using SnapBar.Core;

internal static class ClipboardSelectionPolicyTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        byte[] Chromium(params (string Type, string Value)[] pairs)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(0); writer.Write(pairs.Length);
            foreach (var pair in pairs)
                foreach (var value in new[] { pair.Type, pair.Value })
                {
                    writer.Write(value.Length); writer.Write(Encoding.Unicode.GetBytes(value));
                    while (stream.Position % 4 != 0) writer.Write((byte)0);
                }
            stream.Position = 0; writer.Write((int)stream.Length - 4); return stream.ToArray();
        }
        check("Chromium envelope distinguishes selected text from empty editor line", () =>
            ClipboardSelectionPolicy.IsChromiumSelectionData(Chromium(("vscode-editor-data", "{\"isFromEmptySelection\":false}")))
            && !ClipboardSelectionPolicy.IsChromiumSelectionData(Chromium(("vscode-editor-data", "{\"isFromEmptySelection\":true}"))));
        check("known editors require explicit selection metadata, ordinary custom MIME remains supported", () =>
            ClipboardSelectionPolicy.IsChromiumSelectionData(Chromium(("application/example", "fixture")))
            && !ClipboardSelectionPolicy.IsChromiumSelectionData(Chromium(("application/example", "fixture")), true));
        check("duplicate editor metadata cannot hide empty-line copy", () =>
            !ClipboardSelectionPolicy.IsChromiumSelectionData(Chromium(("vscode-editor-data", "{\"isFromEmptySelection\":false}"),
                ("vscode-editor-data", "{\"isFromEmptySelection\":true}"))));
        check("truncated and invalid Chromium envelopes fail closed without throwing", () =>
        {
            var valid = Chromium(("vscode-editor-data", "{\"isFromEmptySelection\":false}"));
            var overflow = (byte[])valid.Clone(); BitConverter.GetBytes(int.MaxValue).CopyTo(overflow, 8);
            return !ClipboardSelectionPolicy.IsChromiumSelectionData([])
                && !ClipboardSelectionPolicy.IsChromiumSelectionData(valid[..^1])
                && !ClipboardSelectionPolicy.IsChromiumSelectionData(overflow)
                && !ClipboardSelectionPolicy.IsChromiumSelectionData(new byte[1048577]);
        });
    }
}
