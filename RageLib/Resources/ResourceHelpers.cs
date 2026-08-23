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

using System;
using System.Collections.Generic;

namespace RageLib.Resources
{
    public static class ResourceHelpers
    {
        private const int SKIP_SIZE = 64;
        private const int ALIGN_SIZE = 64;

        public static void GetBlocks(IResourceBlock rootBlock, out IList<IResourceBlock> sys, out IList<IResourceBlock> gfx)
        {
            var systemBlocks = new HashSet<IResourceBlock>();
            var graphicBlocks = new HashSet<IResourceBlock>();
            var protectedBlocks = new List<IResourceBlock>();

            var stack = new Stack<IResourceBlock>();
            stack.Push(rootBlock);

            var processed = new HashSet<IResourceBlock>();
            processed.Add(rootBlock);

            while (stack.Count > 0)
            {
                var block = stack.Pop();
                if (block == null)
                    continue;

                if (block is IResourceSystemBlock)
                {
                    if (!systemBlocks.Contains(block))
                        systemBlocks.Add(block);

                    var references = ((IResourceSystemBlock)block).GetReferences();
                    foreach (var reference in references)
                        if (!processed.Contains(reference))
                        {
                            stack.Push(reference);
                            processed.Add(reference);
                        }
                    var subs = new Stack<IResourceSystemBlock>();
                    foreach (var part in ((IResourceSystemBlock)block).GetParts())
                        subs.Push((IResourceSystemBlock)part.Item2);
                    while (subs.Count > 0)
                    {
                        var sub = subs.Pop();

                        foreach (var x in sub.GetReferences())
                            if (!processed.Contains(x))
                            {
                                stack.Push(x);
                                processed.Add(x);
                            }
                        foreach (var x in sub.GetParts())
                            subs.Push((IResourceSystemBlock)x.Item2);

                        protectedBlocks.Add(sub);
                    }

                }
                else
                {
                    if (!graphicBlocks.Contains(block))
                        graphicBlocks.Add(block);
                }
            }

            foreach (var q in protectedBlocks)
                if (systemBlocks.Contains(q))
                    systemBlocks.Remove(q);

            sys = new List<IResourceBlock>();
            foreach (var s in systemBlocks)
                sys.Add(s);
            gfx = new List<IResourceBlock>();
            foreach (var s in graphicBlocks)
                gfx.Add(s);
        }

        public static void AssignPositions(IList<IResourceBlock> blocks, uint basePosition, ref int pageSize, out int pageCount)
        {
            long largestBlockSize = 0;
            foreach (var block in blocks)
            {
                if (largestBlockSize < block.Length)
                    largestBlockSize = block.Length;
            }

            long currentPageSize = 0x2000;
            while (currentPageSize < largestBlockSize)
                currentPageSize *= 2;

            long currentPageCount;
            long currentPosition;
            while (true)
            {
                currentPageCount = 0;
                currentPosition = 0;

                foreach (var block in blocks)
                    block.Position = -1;

                foreach (var block in blocks)
                {
                    if (block.Position != -1)
                        throw new Exception("A position of -1 is not possible!");

                    long maxSpace = currentPageCount * currentPageSize - currentPosition;
                    if (maxSpace < (block.Length + SKIP_SIZE))
                    {
                        currentPageCount++;
                        currentPosition = currentPageSize * (currentPageCount - 1);
                    }

                    block.Position = basePosition + currentPosition;
                    currentPosition += block.Length + SKIP_SIZE;

                    if ((currentPosition % ALIGN_SIZE) != 0)
                        currentPosition += (ALIGN_SIZE - (currentPosition % ALIGN_SIZE));
                }

                if (currentPageCount < 128)
                    break;

                currentPageSize *= 2;
            }

            pageSize = (int)currentPageSize;
            pageCount = (int)currentPageCount;
        }
    }
}
