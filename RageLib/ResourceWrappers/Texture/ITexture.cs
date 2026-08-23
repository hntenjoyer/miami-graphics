/*
    Copyright(c) 2015 Neodymium

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in
    all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.
*/

using System.Collections.Generic;

namespace RageLib.ResourceWrappers
{
    public enum TextureFormat
    {
        D3DFMT_A8R8G8B8 = 21,
        D3DFMT_A1R5G5B5 = 25,
        D3DFMT_A8 = 28,
        D3DFMT_A8B8G8R8 = 32,
        D3DFMT_L8 = 50,

        D3DFMT_DXT1 = 0x31545844,
        D3DFMT_DXT3 = 0x33545844,
        D3DFMT_DXT5 = 0x35545844,
        D3DFMT_ATI1 = 0x31495441,
        D3DFMT_ATI2 = 0x32495441,
        D3DFMT_BC7 = 0x20374342,

        UNKNOWN
    }

    public interface ITextureList : IList<ITexture>
    { }

    public interface ITexture
    {
        string Name { get; set; }

        int Width { get; }

        int Height { get; }

        int MipMapLevels { get; }

        TextureFormat Format { get; }

        int Stride { get; }

        byte[] GetTextureData();

        byte[] GetTextureData(int mipMapLevel);

        void SetTextureData(byte[] data);

        void SetTextureData(byte[] data, int mipMapLevel);

        void Reset(
            int width,
            int height,
            int mipMapLevels,
            int stride,
            TextureFormat Format);
    }
}
