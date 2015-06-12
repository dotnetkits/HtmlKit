﻿//
// HtmlTokenizer.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2015 Xamarin Inc. (www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace HtmlKit {
	public class HtmlTokenizer
	{
		const string AlphaChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
		const string HexAlphabet = "0123456789ABCDEF";
		const string Numeric = "0123456789";
		const string DocType = "doctype";
		const string CData = "[CDATA[";

		readonly HtmlEntityDecoder entity = new HtmlEntityDecoder ();
		readonly StringBuilder data = new StringBuilder ();
		readonly StringBuilder name = new StringBuilder ();
		HtmlDocTypeToken doctype;
		HtmlCommentToken comment;
		HtmlAttribute attribute;
		HtmlTagToken tag;
		char quote;

		TextReader text;

		public HtmlTokenizer (TextReader reader)
		{
			text = reader;
		}

		/// <summary>
		/// Get the current state of the tokenizer.
		/// </summary>
		/// <remarks>
		/// Gets the current state of the tokenizer.
		/// </remarks>
		/// <value>The current state of the tokenizer.</value>
		public HtmlTokenizerState TokenizerState {
			get; private set;
		}

		static bool IsAlphaNumeric (char c)
		{
			return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
		}

		static char ToLower (char c)
		{
			return (c >= 'A' && c <= 'Z') ? (char) (c + 0x20) : c;
		}

		void ClearBuffers ()
		{
			data.Clear ();
			name.Clear ();
		}

		bool ReadDataToken (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					break;
				}

				c = (char) nc;

				switch (c) {
				case '&':
					TokenizerState = HtmlTokenizerState.CharacterReferenceInData;
					return false;
				case '<':
					TokenizerState = HtmlTokenizerState.TagOpen;
					break;
				//case 0: // parse error, but emit it anyway
				default:
					data.Append ((char) c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.Data);

			if (data.Length > 0) {
				token = new HtmlDataToken (data.ToString ());
				data.Clear ();
				return true;
			}

			return false;
		}

		bool ReadCharacterReferenceInData (out HtmlToken token)
		{
			int nc = text.Peek ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				token = new HtmlDataToken (data + "&");
				return true;
			}

			c = (char) nc;
			token = null;

			switch (c) {
			case '\t': case '\n': case '\f': case ' ': case '<': case '&':
				// no character is consumed, emit '&'
				TokenizerState = HtmlTokenizerState.Data;
				data.Append ('&');
				return false;
			default:
//				if (nc == additionalAllowedCharacter) {
//					TokenizerState = HtmlTokenizerState.Data;
//					data.Append ('&');
//					return false;
//				}
				break;
			}

			while (entity.Push (c)) {
				text.Read ();

				if ((nc = text.Peek ()) == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					token = new HtmlDataToken (data + "&" + entity.GetValue ());
					entity.Reset ();
					data.Clear ();
					return true;
				}

				c = (char) nc;
			}

			TokenizerState = HtmlTokenizerState.Data;

			data.Append (entity.GetValue ());
			entity.Reset ();

			if (c == ';') {
				// consume the ';'
				text.Read ();
			}

			return false;
		}

		bool ReadTagOpen (out HtmlToken token)
		{
			int nc = text.Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				token = new HtmlDataToken ("<");
				return true;
			}

			token = null;

			c = (char) nc;

			// Note: we save the data in case we hit a parse error and have to emit a data token
			data.Append ('<');
			data.Append (c);

			switch ((c = (char) nc)) {
			case '!': TokenizerState = HtmlTokenizerState.MarkupDeclarationOpen; return false;
			case '?': TokenizerState = HtmlTokenizerState.BogusComment; return false;
			case '/': TokenizerState = HtmlTokenizerState.EndTagOpen; return false;
			default:
				c = ToLower (c);

				if (c >= 'a' && c <= 'z') {
					TokenizerState = HtmlTokenizerState.TagName;
					name.Append (c);
				} else {
					TokenizerState = HtmlTokenizerState.Data;
					return false;
				}
				break;
			}

			return false;
		}

		bool ReadTagName (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					token = new HtmlDataToken (data.ToString ());
					ClearBuffers ();
					return true;
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\n': case '\f': case ' ':
					TokenizerState = HtmlTokenizerState.BeforeAttributeName;
					break;
				case '/':
					TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					break;
				case '\0':
					name.Append ('\uFFFD');
					break;
				default:
					name.Append (ToLower (c));
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.TagName);

			tag = new HtmlTagToken (name.ToString ());
			name.Clear ();

			return false;
		}

		bool ReadBeforeAttributeName (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					token = new HtmlDataToken (data.ToString ());
					ClearBuffers ();
					tag = null;
					return true;
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\n': case '\f': case ' ':
					break;
				case '/':
					TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
					return false;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					token = tag;
					tag = null;
					return true;
				case '"': case '\'': case '<': case '=':
					// parse error
					goto default;
				case '\0':
					TokenizerState = HtmlTokenizerState.AttributeName;
					name.Append ('\uFFFD');
					return false;
				default:
					TokenizerState = HtmlTokenizerState.AttributeName;
					name.Append (ToLower (c));
					return false;
				}
			} while (true);
		}

		bool ReadSelfClosingStartTag (out HtmlToken token)
		{
			int nc = text.Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				token = new HtmlDataToken (data.ToString ());
				ClearBuffers ();
				return true;
			}

			c = (char) nc;
			token = null;

			// Note: we save the data in case we hit a parse error and have to emit a data token
			data.Append (c);

			if (c != '>') {
				// parse error
				TokenizerState = HtmlTokenizerState.BeforeAttributeName;
			} else {
				TokenizerState = HtmlTokenizerState.Data;
				tag.IsEmptyElement = true;
			}

			return false;
		}

		bool ReadAttributeName (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					token = new HtmlDataToken (data.ToString ());
					ClearBuffers ();
					return true;
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\n': case '\f': case ' ':
					TokenizerState = HtmlTokenizerState.AfterAttributeName;
					break;
				case '/':
					TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					break;
				case '\0':
					name.Append ('\uFFFD');
					break;
				default:
					name.Append (ToLower (c));
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.AttributeName);

			attribute = new HtmlAttribute (name.ToString ());
			tag.Attributes.Add (attribute);
			name.Clear ();

			return false;
		}

		bool ReadAfterAttributeName (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					token = new HtmlDataToken (data.ToString ());
					ClearBuffers ();
					tag = null;
					return true;
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\n': case '\f': case ' ':
					break;
				case '/':
					TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
					return false;
				case '=':
					TokenizerState = HtmlTokenizerState.BeforeAttributeValue;
					return false;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					token = tag;
					tag = null;
					return true;
				case '"': case '\'': case '<':
					// parse error
					goto default;
				case '\0':
					TokenizerState = HtmlTokenizerState.AttributeName;
					name.Append ('\uFFFD');
					return false;
				default:
					TokenizerState = HtmlTokenizerState.AttributeName;
					name.Append (ToLower (c));
					return false;
				}
			} while (true);
		}

		bool ReadBeforeAttributeValue (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					token = new HtmlDataToken (data.ToString ());
					ClearBuffers ();
					tag = null;
					return true;
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\n': case '\f': case ' ':
					break;
				case '"': TokenizerState = HtmlTokenizerState.AttributeValueQuoted; quote = c; return false;
				case '&': TokenizerState = HtmlTokenizerState.AttributeValueUnquoted; return false;
				case '\'': TokenizerState = HtmlTokenizerState.AttributeValueQuoted; quote = c; return false;
				case '/':
					TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
					return false;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					token = tag;
					tag = null;
					return true;
				case '<': case '=': case '`':
					// parse error
					goto default;
				case '\0':
					TokenizerState = HtmlTokenizerState.AttributeName;
					name.Append ('\uFFFD');
					return false;
				default:
					TokenizerState = HtmlTokenizerState.AttributeName;
					name.Append (ToLower (c));
					return false;
				}
			} while (true);
		}

		bool ReadCharacterReferenceInAttributeValue (out HtmlToken token)
		{
			int nc = text.Peek ();
			bool consume;
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				token = new HtmlDataToken (data + "&");
				data.Clear ();
				name.Clear ();
				return true;
			}

			c = (char) nc;
			token = null;

			switch (c) {
			case '\t': case '\n': case '\f': case ' ': case '<': case '&':
				// no character is consumed, emit '&'
				data.Append ('&');
				name.Append ('&');
				consume = false;
				break;
			default:
				//if (nc == additionalAllowedCharacter) {
				//	data.Append ('&');
				//	consume = false;
				//	break;
				//}

				while (entity.Push (c)) {
					text.Read ();

					if ((nc = text.Peek ()) == -1) {
						TokenizerState = HtmlTokenizerState.EndOfFile;
						token = new HtmlDataToken (data + "&" + entity.GetValue ());
						entity.Reset ();
						data.Clear ();
						return true;
					}

					c = (char) nc;
				}

				data.Append (entity.GetValue ());
				consume = c == ';' || c == '=';
				entity.Reset ();
				break;
			}

			if (quote == '\0')
				TokenizerState = HtmlTokenizerState.AttributeValueUnquoted;
			else
				TokenizerState = HtmlTokenizerState.AttributeValueQuoted;

			if (consume)
				text.Read ();

			return false;
		}

		bool ReadAttributeValueUnquoted (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					token = new HtmlDataToken (data.ToString ());
					ClearBuffers ();
					return true;
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\n': case '\f': case ' ':
					TokenizerState = HtmlTokenizerState.BeforeAttributeName;
					break;
				case '&':
					// ReadCharacterReference (true);
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					token = tag;
					tag = null;
					return true;
				case '\'': case '<': case '=': case '`':
					// parse error
					goto default;
				case '\0':
					name.Append ('\uFFFD');
					break;
				default:
					if (c == quote) {
						TokenizerState = HtmlTokenizerState.AfterAttributeValueQuoted;
						break;
					}

					name.Append (c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.AttributeName);

			attribute.Value = name.ToString ();
			name.Clear ();

			return false;
		}

		bool ReadAttributeValueQuoted (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					token = new HtmlDataToken (data.ToString ());
					ClearBuffers ();
					return true;
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '&':
					// ReadCharacterReference (true);
					break;
				case '\0':
					name.Append ('\uFFFD');
					break;
				default:
					if (c == quote) {
						TokenizerState = HtmlTokenizerState.AfterAttributeValueQuoted;
						break;
					}

					name.Append (c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.AttributeName);

			attribute.Value = name.ToString ();
			name.Clear ();

			return false;
		}

		bool ReadAfterAttributeValueQuoted (out HtmlToken token)
		{
			int nc = text.Peek ();
			bool consume;
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				token = new HtmlDataToken (data.ToString ());
				ClearBuffers ();
				return true;
			}

			c = (char) nc;
			token = null;

			switch (c) {
			case '\t': case '\n': case '\f': case ' ':
				TokenizerState = HtmlTokenizerState.BeforeAttributeName;
				consume = true;
				break;
			case '/':
				TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
				consume = true;
				break;
			case '>':
				TokenizerState = HtmlTokenizerState.Data;
				consume = true;
				token = tag;
				tag = null;
				break;
			default:
				TokenizerState = HtmlTokenizerState.BeforeAttributeName;
				consume = false;
				break;
			}

			if (consume)
				text.Read ();

			return token != null;
		}

		bool ReadMarkupDeclarationOpen (out HtmlToken token)
		{
			int count = 0, nc;
			char c = '\0';

			while (count < 2) {
				if ((nc = text.Peek ()) == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					token = new HtmlDataToken (data.ToString ());
					data.Clear ();
					return true;
				}

				if ((c = (char) nc) != '-')
					break;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);
				text.Read ();
				count++;
			}

			token = null;

			if (count == 2) {
				TokenizerState = HtmlTokenizerState.CommentStart;
				comment = new HtmlCommentToken (string.Empty);
				return false;
			}

			if (count == 1) {
				// parse error
				TokenizerState = HtmlTokenizerState.BogusComment;
				return false;
			}

			if (c == 'D' || c == 'd') {
				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);
				text.Read ();
				count = 1;

				while (count < 7) {
					if ((nc = text.Read ()) == -1) {
						TokenizerState = HtmlTokenizerState.EndOfFile;
						token = new HtmlDataToken (data.ToString ());
						data.Clear ();
						return true;
					}

					if (ToLower ((c = (char) nc)) != DocType[count])
						break;

					// Note: we save the data in case we hit a parse error and have to emit a data token
					data.Append (c);
					count++;
				}

				if (count == 7) {
					TokenizerState = HtmlTokenizerState.DocType;
					return false;
				}
			} else if (c == '[') {
				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);
				text.Read ();
				count = 1;

				while (count < 7) {
					if ((nc = text.Read ()) == -1) {
						TokenizerState = HtmlTokenizerState.EndOfFile;
						token = new HtmlDataToken (data.ToString ());
						data.Clear ();
						return true;
					}

					if ((c = (char) nc) != CData[count])
						break;

					// Note: we save the data in case we hit a parse error and have to emit a data token
					data.Append (c);
					count++;
				}

				if (count == 7) {
					TokenizerState = HtmlTokenizerState.CDataSection;
					return false;
				}
			}

			// parse error
			TokenizerState = HtmlTokenizerState.BogusComment;

			return false;
		}

		bool ReadDocType (out HtmlToken token)
		{
			int nc = text.Peek ();
			char c;

			if (nc == -1) {
				token = new HtmlDocTypeToken { ForceQuirks = true };
				TokenizerState = HtmlTokenizerState.EndOfFile;
				data.Clear ();
				return true;
			}

			TokenizerState = HtmlTokenizerState.BeforeDocTypeName;
			c = (char) nc;
			token = null;

			switch (c) {
			case '\t': case '\n': case '\f': case ' ':
				data.Append (c);
				text.Read ();
				break;
			}

			return false;
		}

		bool ReadBeforeDocTypeName (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					token = new HtmlDocTypeToken { ForceQuirks = true };
					TokenizerState = HtmlTokenizerState.EndOfFile;
					data.Clear ();
					return true;
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\n': case '\f': case ' ':
					break;
				case '>':
					token = new HtmlDocTypeToken { ForceQuirks = true };
					TokenizerState = HtmlTokenizerState.Data;
					data.Clear ();
					return true;
				case '\0':
					TokenizerState = HtmlTokenizerState.DocTypeName;
					doctype = new HtmlDocTypeToken ();
					name.Append ('\uFFFD');
					return false;
				default:
					TokenizerState = HtmlTokenizerState.DocTypeName;
					doctype = new HtmlDocTypeToken ();
					name.Append (ToLower (c));
					return false;
				}
			} while (true);
		}

		bool ReadDocTypeName (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.Name = name.ToString ();
					doctype.ForceQuirks = true;
					token = doctype;
					data.Clear ();
					name.Clear ();
					return true;
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\n': case '\f': case ' ':
					TokenizerState = HtmlTokenizerState.AfterDocTypeName;
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					doctype.Name = name.ToString ();
					token = doctype;
					doctype = null;
					data.Clear ();
					name.Clear ();
					return true;
				case '\0':
					name.Append ('\uFFFD');
					break;
				default:
					name.Append (ToLower (c));
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.DocTypeName);

			doctype.Name = name.ToString ();
			name.Clear ();

			return false;
		}

		bool ReadAfterDocTypeName (out HtmlToken token)
		{
			token = null;

			do {
				int nc = text.Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.ForceQuirks = true;
					token = doctype;
					doctype = null;
					data.Clear ();
					return true;
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\n': case '\f': case ' ':
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					token = doctype;
					doctype = null;
					data.Clear ();
					return true;
				default:
					name.Append (ToLower (c));
					if (name.Length < 6)
						break;

					switch (name.ToString ()) {
					case "public": TokenizerState = HtmlTokenizerState.AfterDocTypePublic; return false;
					case "system": TokenizerState = HtmlTokenizerState.AfterDocTypeSystem; return false;
					default: TokenizerState = HtmlTokenizerState.BogusDocType; return false;
					}
				}
			} while (true);
		}

		public bool ReadNextToken (out HtmlToken token)
		{
			do {
				switch (TokenizerState) {
				case HtmlTokenizerState.EndOfFile:
					token = HtmlToken.EndOfFile;
					return true;
				case HtmlTokenizerState.Data:
					if (ReadDataToken (out token))
						return true;
					break;
				case HtmlTokenizerState.CharacterReferenceInData:
					if (ReadCharacterReferenceInData (out token))
						return true;
					break;
				case HtmlTokenizerState.TagOpen:
					if (ReadTagOpen (out token))
						return true;
					break;
				case HtmlTokenizerState.MarkupDeclarationOpen:
					if (ReadMarkupDeclarationOpen (out token))
						return true;
					break;
				case HtmlTokenizerState.DocType:
					if (ReadDocType (out token))
						return true;
					break;
				case HtmlTokenizerState.BeforeDocTypeName:
					if (ReadBeforeDocTypeName (out token))
						return true;
					break;
				case HtmlTokenizerState.DocTypeName:
					if (ReadDocTypeName (out token))
						return true;
					break;
				case HtmlTokenizerState.AfterDocTypeName:
					if (ReadAfterDocTypeName (out token))
						return true;
					break;
				case HtmlTokenizerState.AfterDocTypePublic:
					// TODO
					break;
				case HtmlTokenizerState.AfterDocTypeSystem:
					// TODO
					break;
				case HtmlTokenizerState.BogusDocType:
					// TODO
					break;
				case HtmlTokenizerState.CDataSection:
					// TODO
					break;
				case HtmlTokenizerState.EndTagOpen:
					// TODO
					break;
				case HtmlTokenizerState.TagName:
					if (ReadTagName (out token))
						return true;
					break;
				case HtmlTokenizerState.SelfClosingStartTag:
					if (ReadSelfClosingStartTag (out token))
						return true;
					break;
				case HtmlTokenizerState.BeforeAttributeName:
					if (ReadBeforeAttributeName (out token))
						return true;
					break;
				case HtmlTokenizerState.AttributeName:
					if (ReadBeforeAttributeName (out token))
						return true;
					break;
				case HtmlTokenizerState.AfterAttributeName:
					if (ReadAfterAttributeName (out token))
						return true;
					break;
				case HtmlTokenizerState.BeforeAttributeValue:
					if (ReadBeforeAttributeValue (out token))
						return true;
					break;
				case HtmlTokenizerState.AttributeValueUnquoted:
					if (ReadAttributeValueUnquoted (out token))
						return true;
					break;
				case HtmlTokenizerState.AttributeValueQuoted:
					if (ReadAttributeValueQuoted (out token))
						return true;
					break;
				case HtmlTokenizerState.AfterAttributeValueQuoted:
					if (ReadAfterAttributeValueQuoted (out token))
						return true;
					break;
				case HtmlTokenizerState.CharacterReferenceInAttributeValue:
					if (ReadCharacterReferenceInAttributeValue (out token))
						return true;
					break;
				case HtmlTokenizerState.BogusComment:
					// TODO
					break;
				}
			} while (true);
		}
	}
}
