using System.Text;

public static class GameText
{
    public static string Decode(ReadOnlySpan<byte> text)
    {
        var result = new StringBuilder(text.Length);
        foreach (byte value in text)
        {
            if (value == 0x50) break;
            char c = value switch
            {
                >= 0x05 and <= 0x13 => "ガギグゲゴザジズゼゾダヂヅデド"[value - 0x05],
                >= 0x19 and <= 0x1c => "バビブボ"[value - 0x19],
                >= 0x26 and <= 0x34 => "がぎぐげござじずぜぞだぢづでど"[value - 0x26],
                >= 0x3a and <= 0x3e => "ばびぶべぼ"[value - 0x3a],
                >= 0x40 and <= 0x48 => "パピプポぱぴぷぺぽ"[value - 0x40],
                0x6e => 'ぃ', 0x6f => 'ぅ',
                >= 0x70 and <= 0x78 => "「」『』・…ぁぇぉ"[value - 0x70],
                >= 0x79 and <= 0x7e => "┌─┐│└┘"[value - 0x79],
                0x7f => '　',
                >= 0x80 and <= 0xb0 => "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフホマミムメモヤユヨラルレロワヲンッャュョィ"[value - 0x80],
                >= 0xb1 and <= 0xe5 => "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをんっゃゅょーﾟﾞ"[value - 0xb1],
                >= 0xe6 and <= 0xeb => "？！。ァゥェ"[value - 0xe6],
                >= 0xec and <= 0xf5 => "▷▶▼♂円×．／ォ♀"[value - 0xec],
                >= 0xf6 => (char)('０' + value - 0xf6),
                _ => '〓'
            };
            result.Append(c);
        }
        for (int i = 0; i < result.Length; i++)
        {
            if (result[i] is not ('へ' or 'べ' or 'ぺ' or 'り')) continue;
            int kind = 0;
            for (int distance = 1; distance < result.Length && kind == 0; distance++)
            {
                foreach (int j in new[] { i - distance, i + distance })
                {
                    if (j < 0 || j >= result.Length) continue;
                    char c = result[j];
                    if (c is 'へ' or 'べ' or 'ぺ' or 'り' or 'ヘ' or 'ベ' or 'ペ' or 'リ' or 'ー') continue;
                    if (c >= 'ぁ' && c <= 'ゖ') kind = 1;
                    else if (c >= 'ァ' && c <= 'ヺ') kind = 2;
                    if (kind != 0) break;
                }
            }
            if (kind == 2) result[i] = (char)(result[i] + 0x60);
        }
        return result.ToString();
    }
}
