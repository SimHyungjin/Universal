using System;
using System.Security.Cryptography;

public static class DataEncryption
{
    // 프로덕션에서는 서버 수신 또는 기기 ID 파생 방식으로 교체 권장
    private static readonly byte[] Key =
    {
        0x4A,0x8F,0x3C,0xD2,0x71,0xBE,0x90,0x15,
        0xA3,0x6E,0xF0,0x27,0xCC,0x84,0x59,0x3B,
        0xD8,0x1A,0x7F,0x46,0xE5,0x02,0xB9,0x6D,
        0x33,0xAC,0x78,0xF4,0x21,0x9E,0x50,0xC7
    };

    public static byte[] Encrypt(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key     = Key;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        byte[] cipher = encryptor.TransformFinalBlock(data, 0, data.Length);

        byte[] result = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0,             aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);
        return result;
    }

    public static byte[] Decrypt(byte[] data)
    {
        const int ivLen = 16;

        using var aes = Aes.Create();
        aes.Key     = Key;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] iv = new byte[ivLen];
        Buffer.BlockCopy(data, 0, iv, 0, ivLen);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, ivLen, data.Length - ivLen);
    }
}
