namespace GTFormats.Fbx
{
    // Targa writer: uncompressed 32-bit BGRA, origin top-left, 8 alpha bits.
    public static class Tga
    {
        public static void Write(string path, byte[] rgba, int w, int h)
        {
            byte[] file = new byte[18 + (w * h * 4)];
            file[2] = 2;
            file[12] = (byte) w;
            file[13] = (byte) (w >> 8);
            file[14] = (byte) h;
            file[15] = (byte) (h >> 8);
            file[16] = 32;
            file[17] = 0x28;
            for (int i = 0; i < w * h; i++)
            {
                int o = 18 + (i * 4);
                file[o] = rgba[(i * 4) + 2];
                file[o + 1] = rgba[(i * 4) + 1];
                file[o + 2] = rgba[i * 4];
                file[o + 3] = rgba[(i * 4) + 3];
            }
            System.IO.File.WriteAllBytes(path, file);
        }
    }
}
