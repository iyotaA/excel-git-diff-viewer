using System.Collections.Generic;
using System.IO;

namespace ExcelGitDiffViewer.Services;

/// <summary>
/// MS-OVBA 2.4.1 の CompressedContainer 展開アルゴリズム。
/// VBA モジュールストリーム内の圧縮ソースを元のバイト列へ復元する。
/// </summary>
public static class VbaDecompression
{
    /// <summary>
    /// <paramref name="data"/> の <paramref name="start"/> 位置（シグネチャバイト 0x01）から
    /// CompressedContainer を展開する。不正な構造なら例外。
    /// </summary>
    public static byte[] Decompress(byte[] data, int start)
    {
        if (start >= data.Length || data[start] != 0x01)
        {
            throw new InvalidDataException("CompressedContainer のシグネチャ (0x01) がありません。");
        }

        var output = new List<byte>(data.Length * 2);
        int pos = start + 1;

        while (pos + 1 < data.Length)
        {
            int header = data[pos] | (data[pos + 1] << 8);
            pos += 2;

            int chunkSize = (header & 0x0FFF) + 3; // ヘッダ2バイトを含むチャンク全体のバイト数
            int signature = (header >> 12) & 0x07;
            int compressedFlag = (header >> 15) & 0x01;

            if (signature != 0b011)
            {
                throw new InvalidDataException("CompressedChunkSignature が不正です。");
            }

            int chunkDataEnd = pos + (chunkSize - 2);
            if (chunkDataEnd > data.Length)
            {
                chunkDataEnd = data.Length;
            }

            int chunkStartOut = output.Count;

            if (compressedFlag == 0)
            {
                // 非圧縮チャンク: 4096 バイトをそのままコピー。
                for (int i = 0; i < 4096 && pos < chunkDataEnd; i++)
                {
                    output.Add(data[pos++]);
                }

                continue;
            }

            while (pos < chunkDataEnd)
            {
                byte flagByte = data[pos++];
                for (int bit = 0; bit < 8 && pos < chunkDataEnd; bit++)
                {
                    if ((flagByte & (1 << bit)) == 0)
                    {
                        // リテラルトークン
                        output.Add(data[pos++]);
                    }
                    else
                    {
                        // コピートークン
                        if (pos + 1 >= data.Length)
                        {
                            return output.ToArray();
                        }

                        int token = data[pos] | (data[pos + 1] << 8);
                        pos += 2;

                        int difference = output.Count - chunkStartOut;
                        int bitCount = System.Math.Max(CeilLog2(difference), 4);
                        int lengthMask = 0xFFFF >> bitCount;
                        int length = (token & lengthMask) + 3;
                        int offset = (token >> (16 - bitCount)) + 1;

                        int src = output.Count - offset;
                        if (src < 0)
                        {
                            throw new InvalidDataException("コピートークンのオフセットが不正です。");
                        }

                        for (int k = 0; k < length; k++)
                        {
                            output.Add(output[src + k]);
                        }
                    }
                }
            }
        }

        return output.ToArray();
    }

    /// <summary>ceil(log2(value))。value&lt;=1 では 0。</summary>
    private static int CeilLog2(int value)
    {
        int bits = 0;
        int v = value - 1;
        while (v > 0)
        {
            bits++;
            v >>= 1;
        }

        return bits;
    }
}
