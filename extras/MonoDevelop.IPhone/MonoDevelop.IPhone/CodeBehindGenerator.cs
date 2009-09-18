// 
// CodeBehindGenerator.cs
//  
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
// 
// Copyright (c) 2009 Novell, Inc. (http://www.novell.com)
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

using System;
using System.CodeDom;
using System.Xml.Linq;
using System.Linq;
using System.Collections.Generic;
using System.Xml;
using System.Text;
using MonoDevelop.IPhone.InterfaceBuilder;
using System.CodeDom.Compiler;
using System.IO;

namespace MonoDevelop.IPhone
{
	
	static class CodeBehindGenerator
	{
		
		public static IEnumerable<CodeTypeDeclaration> GetTypes (XDocument xibDoc, CodeDomProvider provider, CodeGeneratorOptions generatorOptions)
		{
			var ibDoc = IBDocument.Deserialize (xibDoc);
			
			object outVar;
			UnknownIBObject objects;
			if (!ibDoc.Properties.TryGetValue ("IBDocument.Objects", out outVar) || (objects = outVar as UnknownIBObject) == null)
				return new CodeTypeDeclaration[0];
			
			//process the connection records
			NSMutableArray connectionRecords;
			if (!objects.Properties.TryGetValue ("connectionRecords", out outVar) || (connectionRecords = outVar as NSMutableArray) == null)
				return new CodeTypeDeclaration[0];
			
			//group connection records by type ref ID
			var typeRecords = new Dictionary<int,List<IBConnectionRecord>> ();
			foreach (var record in connectionRecords.Values.OfType<IBConnectionRecord> ()) {
				//get the type this member belongs in
				var ev = record.Connection as IBActionConnection;
				var outlet = record.Connection as IBOutletConnection;
				if (outlet == null && ev == null) {
					//not a recognised connection type. probably a desktop xib
					continue;
				}
				int? typeIndex = ((IBObject)(ev != null
					? ev.Destination.Reference
					: outlet.Source.Reference)).Id;
				if (typeIndex == null)
					throw new InvalidOperationException ("Connection " + record.ConnectionId + " references null object ID");
				List<IBConnectionRecord> records;
				if (!typeRecords.TryGetValue (typeIndex.Value, out records))
					typeRecords[typeIndex.Value] = records = new List<IBConnectionRecord> ();
				records.Add (record);
			}
			
			//grab the custom class names, keyed by object ID
			var classNames = new Dictionary<int, string> ();
			var flattenedProperties = (NSMutableDictionary) objects.Properties ["flattenedProperties"];
			foreach (var pair in flattenedProperties.Values) {
				string keyStr = (string)pair.Key;
				if (!keyStr.EndsWith (".CustomClassName"))
					continue;
				int key = int.Parse (keyStr.Substring (0, keyStr.IndexOf ('.')));
				string name = (string)pair.Value;
				
				//HACK: why does IB not generate partial classes for UIApplication or UIResponder? I guess we should suppress them too
				if (name == "UIApplication" || name == "UIResponder")
					continue;
				
				classNames[key] = (string)pair.Value;
			}
			
			// it seems to be hard to figure out which objects we should generate classes for,
			// so take the list of classes that xcode would generate
			var ibApprovedPartialClassNames = new HashSet<string> ();
			UnknownIBObject classDescriber;
			if (ibDoc.Properties.TryGetValue ("IBDocument.Classes", out outVar) && (classDescriber = outVar as UnknownIBObject) != null) {
				NSMutableArray arr;
				if (classDescriber.Properties.TryGetValue ("referencedPartialClassDescriptions", out outVar) && (arr = outVar as NSMutableArray) != null) {
					foreach (var cls in arr.Values.OfType<IBPartialClassDescription> ())
						if (!String.IsNullOrEmpty (cls.ClassName))
						    ibApprovedPartialClassNames.Add (cls.ClassName);
				}
			}
			
			// construct the type objects, keyed by ref ID
			var objectRecords = (IBMutableOrderedSet) objects.Properties ["objectRecords"];
			var types = new Dictionary<int,CodeTypeDeclaration> ();
			foreach (IBObjectRecord record in objectRecords.OrderedObjects.OfType<IBObjectRecord> ()) {
				string name;
				int? objId = ((IBObject)ResolveIfReference (record.Object)).Id;
				if (objId != null && classNames.TryGetValue (record.ObjectId, out name) && ibApprovedPartialClassNames.Contains (name)) {
					var type = new CodeTypeDeclaration (name) {
						IsPartial = true
					};
					type.CustomAttributes.Add (
						new CodeAttributeDeclaration ("MonoTouch.Foundation.Register",
							new CodeAttributeArgument (new CodePrimitiveExpression (name))));
					
					//FIXME: implement proper base class resolution. I'm not sure where the info is - it might need some
					// inference rules
					
					var obj = ResolveIfReference (record.Object);
					if (obj != null) {
						string baseType = "MonoTouch.Foundation.NSObject";
						if (obj is IBProxyObject) {
							baseType = "MonoTouch.UIKit.UIViewController";
						} else if (obj is UnknownIBObject) {
							var uobj = (UnknownIBObject)obj;
							
							//if the item comes from another nib, don't generate the partial class in this xib's codebehind
							if (uobj.Properties.ContainsKey ("IBUINibName") && !String.IsNullOrEmpty (uobj.Properties["IBUINibName"] as string))
								continue;
							
							if (uobj.Class != "IBUICustomObject")
								baseType = GetTypeName (uobj.Class);
						}
						type.Comments.Add (new CodeCommentStatement (String.Format ("Base type probably should be {0} or subclass", baseType))); 
					}
					
					types.Add (objId.Value, type);
				}
			}
			
			
			foreach (KeyValuePair<int,List<IBConnectionRecord>> typeRecord in typeRecords) {
				CodeTypeDeclaration type;
				if (!types.TryGetValue (typeRecord.Key, out type))
					continue;
				
				//separate out the actions and outlets
				var actions = new List<IBActionConnection> ();
				var outlets = new List<IBOutletConnection> ();
				foreach (var record in typeRecord.Value) {
					if (record.Connection is IBActionConnection)
						actions.Add ((IBActionConnection)record.Connection);
					else if (record.Connection is IBOutletConnection)
						outlets.Add ((IBOutletConnection)record.Connection);
				}
				
				//process the actions, grouping ones with the same name
				foreach (var actionGroup in actions.GroupBy (a => a.Label)) {
					//find a common sender type for all the items in the grouping
					CodeTypeReference senderType = null;
					foreach (IBActionConnection ev in actionGroup) {
						var sender = ResolveIfReference (ev.Source) as UnknownIBObject;
						var newType = sender != null
							? new CodeTypeReference (GetTypeName (sender.Class))
							: new CodeTypeReference ("MonoTouch.Foundation.NSObject");
						if (senderType == null) {
							senderType = newType;
							continue;
						} else if (senderType == newType) {
							continue;
						} else {
							//FIXME: resolve common type
							newType = new CodeTypeReference ("MonoTouch.Foundation.NSObject");
							break;
						}	
					}
					
					//create the action method and add it
					StringWriter actionStubWriter = null;
					GenerateAction (type, actionGroup.Key, senderType, provider, generatorOptions, ref actionStubWriter);
					if (actionStubWriter != null) {
						type.Comments.Add (new CodeCommentStatement (actionStubWriter.ToString ()));
						actionStubWriter.Dispose ();
					}
				}
				
				foreach (var outlet in outlets) {
					CodeTypeReference outletType;
					//destination is widget, so get type
					var widget = outlet.Destination.Reference as UnknownIBObject;
					if (widget != null)
						outletType = new CodeTypeReference (GetTypeName (widget.Class));
					else
						outletType = new CodeTypeReference ("System.Object");
					
					type.Members.Add (CreateOutletProperty (outlet.Label, outletType));
				}
			}
			
			return types.Values;
		}

		static void GenerateAction (CodeTypeDeclaration type, string name, CodeTypeReference senderType, CodeDomProvider provider,
		                            CodeGeneratorOptions generatorOptions, ref StringWriter actionStubWriter)
		{	
			if (provider is Microsoft.CSharp.CSharpCodeProvider) {
				type.Members.Add (new CodeSnippetTypeMember ("[MonoTouch.Foundation.Export(\"" + name + "\")]"));
				type.Members.Add (new CodeSnippetTypeMember (
					String.Format ("partial void {1} ({2} sender);\n",
					               name, provider.CreateValidIdentifier (name.TrimEnd (':')), senderType.BaseType)));
				return;
			}
			
			var meth = CreateEventMethod (name, senderType);
			
			bool actionStubWriterCreated = false;
			if (actionStubWriter == null) {
				actionStubWriterCreated = true;
				actionStubWriter = new StringWriter ();
				actionStubWriter.WriteLine ("Action method stubs:");
				actionStubWriter.WriteLine ();
			}
			try {
				provider.GenerateCodeFromMember (meth, actionStubWriter, generatorOptions);
				actionStubWriter.WriteLine ();
			} catch {
				//clear the header if generation failed
				if (actionStubWriterCreated)
					actionStubWriter = null;
			}
		}

		
		static string GetTypeName (string ibType)
		{
			if (ibType.StartsWith ("NS")) {
				return "MonoTouch.Foundation." + ibType;
			} else if (ibType.StartsWith ("IB") && ibType.Length > 2) {
				string name = ibType.Substring (2);
				if (name.StartsWith ("UI"))
					return "MonoTouch.UIKit." + name;
				if (name.StartsWith ("MK"))
					return "MonoTouch.MapKit." + name;
			}
			return "MonoTouch.Foundation.NSObject";
		}
		
		public static CodeMemberProperty CreateOutletProperty (string name, CodeTypeReference typeRef)
		{
			var prop = new CodeMemberProperty () {
				Name = name,
				Type = typeRef
			};
			prop.CustomAttributes.Add (
				new CodeAttributeDeclaration ("MonoTouch.Foundation.Connect",
			    		new CodeAttributeArgument (new CodePrimitiveExpression (name))));
			prop.SetStatements.Add (
				new CodeMethodInvokeExpression (
					new CodeMethodReferenceExpression (
						new CodeThisReferenceExpression (), "SetNativeField"),
						new CodePrimitiveExpression (name),
						new CodePropertySetValueReferenceExpression ()));
			prop.GetStatements.Add (
				new CodeMethodReturnStatement (
					new CodeCastExpression (typeRef,
						new CodeMethodInvokeExpression (
							new CodeMethodReferenceExpression (
								new CodeThisReferenceExpression (), "GetNativeField"),
								new CodePrimitiveExpression (name)))));
			prop.Attributes = (prop.Attributes & ~MemberAttributes.AccessMask) | MemberAttributes.Private;
			return prop;
		}
		
		public static CodeTypeMember CreateEventMethod (string name, CodeTypeReference senderType)
		{
			var meth = new CodeMemberMethod () {
				Name = name.TrimEnd (':'),
				ReturnType = new CodeTypeReference (typeof (void)),
			};
			meth.Parameters.Add (
				new CodeParameterDeclarationExpression () {
					Name = "sender",
					Type = senderType }
			);
			
			meth.CustomAttributes.Add (
				new CodeAttributeDeclaration ("MonoTouch.Foundation.Export",
					new CodeAttributeArgument (new CodePrimitiveExpression (name))));
			
			return meth;
		}
		
		static object ResolveIfReference (object o)
		{
			IBReference r = o as IBReference;
			if (r != null)
				return ResolveIfReference (r.Reference);
			else
				return o;
		}
	}
}
