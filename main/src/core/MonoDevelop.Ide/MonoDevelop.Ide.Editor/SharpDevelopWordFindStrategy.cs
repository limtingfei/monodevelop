//
// SharpDevelopWordFindStrategy.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (C) 2008 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;

namespace MonoDevelop.Ide.Editor
{
	class SharpDevelopWordFindStrategy : WordFindStrategy
	{
		int FindNextWordOffset (IDocument doc, int offset, bool subword)
		{
			int lineNumber   = doc.OffsetToLineNumber (offset);
			var line = doc.GetLine (lineNumber);
			if (line == null)
				return offset;
			
			int result    = offset;
			int endOffset = line.Offset + line.Length;
			if (result == endOffset) {
				line = doc.GetLine (lineNumber + 1);
				if (line != null)
					result = line.Offset;
				return result;
			}
			
			CharacterClass current = GetCharacterClass (doc.GetCharAt (result), subword, false);
			while (result < endOffset) {
				CharacterClass next = GetCharacterClass (doc.GetCharAt (result), subword, false);
				if (next != current) {
					
					// camelCase and PascalCase handling
					bool camelSkip = false;
					if (next == CharacterClass.LowercaseLetter && current == CharacterClass.UppercaseLetter) {
						if (result-2 > line.Offset) {
							CharacterClass previous = GetCharacterClass (doc.GetCharAt (result-2), subword, false);
							if (previous == CharacterClass.UppercaseLetter && result-2 > offset)
								result--;
							else
								camelSkip = true;
						}
					}
					
					if (!camelSkip)
						break;
				}
				
				current = next;		
				result++;
			}
			while (result < endOffset && GetCharacterClass (doc.GetCharAt (result), subword, false) == CharacterClass.Whitespace) {
				result++;
			}
			return result;
		}
		
		int FindPrevWordOffset (IDocument doc, int offset, bool subword)
		{
			int lineNumber = doc.OffsetToLineNumber (offset);
			var line = doc.GetLine (lineNumber);
			if (line == null)
				return offset;
			
			int result = offset;
			if (result == line.Offset) {
				line = doc.GetLine (lineNumber - 1);
				if (line != null)
					result = line.Offset + line.Length;
				return result;
			}
			
			CharacterClass current = GetCharacterClass (doc.GetCharAt (result - 1), subword, false);
			
			if (current == CharacterClass.Whitespace && result - 1 > line.Offset) {
				result--;
				current = GetCharacterClass (doc.GetCharAt (result - 2), subword, false);
			}
			
			while (result > line.Offset) {
				CharacterClass prev = GetCharacterClass (doc.GetCharAt (result - 1), subword, false);
				if (prev != current) {
					
					// camelCase and PascalCase handling
					bool camelSkip = false;
					if (prev == CharacterClass.UppercaseLetter && current == CharacterClass.LowercaseLetter) {
						if (result-2 > line.Offset) {
							CharacterClass back2 = GetCharacterClass (doc.GetCharAt (result-2), subword, false);
							if (back2 == CharacterClass.UppercaseLetter)
								result--;
							else
								camelSkip = true;
						}
					}
					
					if (!camelSkip)
						break;
				}
				
				current = prev;
				result--;
			}
			
			return result;
		}
		
		public override int FindNextWordOffset (IDocument doc, int offset)
		{
			return FindNextWordOffset (doc, offset, false);
		}
		
		public override int FindPrevWordOffset (IDocument doc, int offset)
		{
			return FindPrevWordOffset (doc, offset, false);
		}
		
		public override int FindNextSubwordOffset (IDocument doc, int offset)
		{
			return FindNextWordOffset (doc, offset, true);
		}
		
		public override int FindPrevSubwordOffset (IDocument doc, int offset)
		{
			return FindPrevWordOffset (doc, offset, true);
		}
	}
}
