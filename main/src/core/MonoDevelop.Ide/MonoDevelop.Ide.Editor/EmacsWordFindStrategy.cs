//
// EmacsWordFindStrategy.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (C) 2008 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated docation files (the
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
using CC = MonoDevelop.Ide.Editor.WordFindStrategy.CharacterClass;
using SW = MonoDevelop.Ide.Editor.WordFindStrategy;

namespace MonoDevelop.Ide.Editor
{
	class EmacsWordFindStrategy : WordFindStrategy
	{
		bool treat_;
		
		public EmacsWordFindStrategy (bool treat_)
		{
			this.treat_ = treat_;
		}
		
		int FindNextWordOffset (IDocument doc, int offset, bool subword)
		{
			if (offset + 1 >= doc.TextLength)
				return doc.TextLength;
			int result = offset + 1;
			CC previous = SW.GetCharacterClass (doc.GetCharAt (result), subword, treat_);
			bool inIndentifier = previous != CC.Unknown && previous != CC.Whitespace;			
			while (result < doc.TextLength) {
				char ch = doc.GetCharAt (result);
				CC current = SW.GetCharacterClass (ch, subword, treat_);
				
				//camelCase / PascalCase splitting
				if (subword) {
					if (current == CC.Digit && (previous != CC.Digit || (result-1 == offset && !Char.IsDigit (doc.GetCharAt (result-1))))) {
						break;
					} else if (previous == CC.Digit && current != CC.Digit) {
						break;
					} else if (current == CC.UppercaseLetter && previous != CC.UppercaseLetter) {
						break;
					} else if (current == CC.LowercaseLetter && previous == CC.UppercaseLetter && result - 2 > 0
					           && SW.GetCharacterClass (doc.GetCharAt (result - 2), subword, treat_) != CC.LowercaseLetter)
					{
						result--;
						break;
					}
				}
				
				//else break at end of identifiers
				if (previous != CC.Unknown && previous != CC.Whitespace) {
					inIndentifier = true;
				} else if (inIndentifier) {
					result--;
					break;
				}
				previous = current;
				result++;
			}
			foreach (var segment in doc.GetFoldingsFromOffset (result)) {
				if (segment.IsFolded)
					result = System.Math.Max (result, segment.EndOffset);
			}
			return result;
		}
		
		int FindPrevWordOffset (IDocument doc, int offset, bool subword)
		{
			if (offset <= 0)
				return 0;
			int  result = offset - 1;
			CC previous = SW.GetCharacterClass (doc.GetCharAt (result), subword, treat_);
			bool inIndentifier = previous != CC.Unknown && previous != CC.Whitespace;			
			while (result > 0) {
				char ch = doc.GetCharAt (result);
				CC current = SW.GetCharacterClass (ch, subword, treat_);
				
				//camelCase / PascalCase splitting
				if (subword) {
					if (current == CC.Digit && previous != CC.Digit) {
						result++;
						break;
					} else if (previous == CC.Digit && current != CC.Digit) {
						result++;
						break;
					} else if (current == CC.UppercaseLetter && previous != CC.UppercaseLetter) {
						break;
					} else if (current == CC.LowercaseLetter && previous == CC.UppercaseLetter && result + 2 < doc.TextLength
					           && SW.GetCharacterClass (doc.GetCharAt (result + 2), subword, treat_) != CC.LowercaseLetter)
					{
						result++;
						break;
					}
				}
				
				//else break at end of identifiers
				if (previous != CC.Unknown && previous != CC.Whitespace) {
					inIndentifier = true;
				} else if (inIndentifier) {
					result += 2;
					break;
				}
				previous = current;
				result--;
			}
			foreach (var segment in doc.GetFoldingsFromOffset (result)) {
				if (segment.IsFolded)
					result = System.Math.Min (result, segment.Offset);
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
