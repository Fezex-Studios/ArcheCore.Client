using System;
using System.IO;
using System.Security.Cryptography;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// Decrypts the gamedata content package at runtime. Format on disk:
    /// [16-byte IV][AES-256-CBC ciphertext].
    ///
    /// IMPORTANT — what this actually protects against: casual browsing of
    /// unreleased quest/item/dialog text by opening the file in a generic
    /// tool. It does NOT protect against a determined reverse engineer —
    /// the key ships inside the IL2CPP binary because the client has to be
    /// able to decrypt the file to play the game, same fundamental limit as
    /// any client-side DRM. Don't treat this as "secure," treat it as "not
    /// trivially openable by double-clicking the file." Switching the
    /// payload from a SQLite file to the custom binary format in
    /// GameDataBinaryFormat.cs doesn't change that threat model — it's
    /// exactly as secure as this key staying private, same as before.
    ///
    /// Key must exactly match whatever key the Editor's "Export Binary +
    /// Encrypt" step (or encrypt_gamedata.py, if you still use that
    /// standalone) used to produce the file shipped in StreamingAssets /
    /// on the Authserver.
    /// </summary>
    public static class GameDataCrypto
    {
        // 32 bytes = AES-256. Regenerate this (and re-export/re-encrypt
        // gamedata) any time you suspect it's leaked — it's only a
        // deterrent, so rotating it occasionally costs nothing and
        // invalidates any previously-extracted key.
        private static readonly byte[] Key =
        {
            0x4B, 0x1C, 0x9E, 0x7A, 0x2D, 0x88, 0x3F, 0x61,
            0xA5, 0x0E, 0xD2, 0x77, 0x9B, 0x44, 0x1A, 0xC3,
            0x6F, 0x52, 0xE8, 0x09, 0xB1, 0x3D, 0x95, 0x2C,
            0x70, 0xF4, 0x18, 0x8A, 0x5C, 0xDB, 0x21, 0x67
        };

        /// <summary>
        /// Decrypts an encrypted gamedata blob and returns the plaintext
        /// bytes directly — no temp file involved, so nothing plaintext
        /// ever touches disk on the client. Throws InvalidDataException if
        /// the data is too short to contain an IV; a wrong key just
        /// produces garbage bytes or a padding exception, which
        /// GameDataBinaryFormat.ReadItems will catch as a bad magic number.
        /// </summary>
        public static byte[] Decrypt(byte[] fileBytes)
        {
            const int ivLength = 16;
            if (fileBytes.Length <= ivLength)
                throw new InvalidDataException("Data too short to contain an IV.");

            byte[] iv = new byte[ivLength];
            Buffer.BlockCopy(fileBytes, 0, iv, 0, ivLength);

            byte[] cipherText = new byte[fileBytes.Length - ivLength];
            Buffer.BlockCopy(fileBytes, ivLength, cipherText, 0, cipherText.Length);

            using Aes aes = Aes.Create();
            aes.Key     = Key;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            using MemoryStream     output    = new MemoryStream();
            using (CryptoStream crypto = new CryptoStream(
                       new MemoryStream(cipherText), decryptor, CryptoStreamMode.Read))
            {
                crypto.CopyTo(output);
            }

            return output.ToArray();
        }
    }
}