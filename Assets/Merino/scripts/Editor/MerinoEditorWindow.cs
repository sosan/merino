using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Windows;
using Yarn;
using Yarn.Unity;
using File = System.IO.File;

namespace Merino
{

	class MerinoEditorWindow : EditorWindow
	{
		[NonSerialized] bool m_Initialized;
		[SerializeField] TreeViewState viewState; // Serialized in the window layout file so it survives assembly reloading
		[SerializeField] MultiColumnHeaderState m_MultiColumnHeaderState;
		[SerializeField] bool useAutosave = false;
		bool doubleClickOpensFile = true;
		
		[NonSerialized] float sidebarWidth = 200f;
		[SerializeField] Vector2 scrollPos;
		
		SearchField m_SearchField;
		MerinoTreeView m_TreeView;
		MyTreeAsset m_MyTreeAsset;

		TextAsset currentFile;

		[MenuItem("Window/Merino (Yarn Editor)")]
		public static MerinoEditorWindow GetWindow ()
		{
			var window = GetWindow<MerinoEditorWindow>();
			window.titleContent = new GUIContent("Merino (Yarn)");
			window.Focus();
			window.Repaint();
			return window;
		}

		[OnOpenAsset]
		public static bool OnOpenAsset (int instanceID, int line)
		{
//			var myTreeAsset = EditorUtility.InstanceIDToObject (instanceID) as MyTreeAsset;
//			if (myTreeAsset != null)
//			{
//				var window = GetWindow ();
//				window.SetTreeAsset(myTreeAsset);
//				return true;
//			}
			var myTextAsset = EditorUtility.InstanceIDToObject(instanceID) as TextAsset;
			if (myTextAsset != null)
			{
				var window = GetWindow ();
				return window.SetTreeAsset(myTextAsset);
			}
			return false; // we did not handle the open
		}

		bool SetTreeAsset (TextAsset myTextAsset)
		{
			if (doubleClickOpensFile && IsProbablyYarnFile(myTextAsset))
			{
				currentFile = myTextAsset;
				m_Initialized = false;
				return true;
			}
			else
			{
				return false;
			}
		}

		Rect multiColumnTreeViewRect
		{
			get { return new Rect(20, 30, sidebarWidth, position.height-60); }
		}

		Rect toolbarRect
		{
			get { return new Rect (20f, 10f, sidebarWidth, 20f); }
		}

		Rect nodeEditRect
		{
			get { return new Rect( sidebarWidth+40f, 10, position.width-sidebarWidth-70, position.height-30);}
		}

		Rect bottomToolbarRect
		{
			get { return new Rect(20f, position.height - 18f, position.width - 40f, 16f); }
		}

		public MerinoTreeView treeView
		{
			get { return m_TreeView; }
		}

		void InitIfNeeded ()
		{
			if (m_Initialized) return;
			
			// Check if it already exists (deserialized from window layout file or scriptable object)
			if (viewState == null)
				viewState = new TreeViewState();

			bool firstInit = m_MultiColumnHeaderState == null;
			var headerState = MerinoTreeView.CreateDefaultMultiColumnHeaderState(multiColumnTreeViewRect.width);
			if (MultiColumnHeaderState.CanOverwriteSerializedFields(m_MultiColumnHeaderState, headerState))
				MultiColumnHeaderState.OverwriteSerializedFields(m_MultiColumnHeaderState, headerState);
			m_MultiColumnHeaderState = headerState;
				
			var multiColumnHeader = new MyMultiColumnHeader(headerState);
			if (firstInit)
				multiColumnHeader.ResizeToFit ();

			var treeModel = new TreeModel<MerinoTreeElement>(GetData());
				
			m_TreeView = new MerinoTreeView(viewState, multiColumnHeader, treeModel);

			m_SearchField = new SearchField();
			m_SearchField.downOrUpArrowKeyPressed += m_TreeView.SetFocusAndEnsureSelectedItem;

			m_Initialized = true;
		}
		
		IList<MerinoTreeElement> GetData ()
		{
//			if (m_MyTreeAsset != null && m_MyTreeAsset.treeElements != null && m_MyTreeAsset.treeElements.Count > 0)
//				return m_MyTreeAsset.treeElements;


			var treeElements = new List<MerinoTreeElement>();
			var root = new MerinoTreeElement("Root", -1, 0);
			treeElements.Add(root);
			
			// extract data from the text file
			if (currentFile != null)
			{
				AssetDatabase.Refresh();
				//var format = YarnSpinnerLoader.GetFormatFromFileName(AssetDatabase.GetAssetPath(currentFile));
				var nodes = YarnSpinnerLoader.GetNodesFromText(currentFile.text, NodeFormat.Text);
				foreach (var node in nodes)
				{
					// clean some of the stuff to help prevent file corruption
					string cleanName = CleanYarnField(node.title, true);
					string cleanBody = CleanYarnField(node.body);
					string cleanTags = CleanYarnField(node.tags, true);
					// write data to the objects
					var newItem = new MerinoTreeElement( cleanName ,0,treeElements.Count);
					newItem.nodeBody = cleanBody;
					newItem.nodePosition = new Vector2Int(node.position.x, node.position.y);
					newItem.nodeTags = cleanTags;
					treeElements.Add(newItem);
				}
			}
			else 
			{ // generate default data
				var start = new MerinoTreeElement("Start", 0, 1);
				start.nodeBody = "This is the Start node. Write the beginning of your Yarn story here.";
				treeElements.Add(start);
			}

			return treeElements;
		}

		string CleanYarnField(string inputString, bool extraClean=false)
		{
			if (extraClean)
			{
				return inputString.Replace("===", " ").Replace("---", " ").Replace("title:", " ").Replace("tags:", " ").Replace("position:", " ").Replace("colorID:", " ");
			}
			else
			{
				return inputString.Replace("===", " ").Replace("---", " ");
			}
		}

		// writes data to the file
		void SaveDataToFile()
		{
			if (currentFile != null)
			{
				var nodeInfo = new List<YarnSpinnerLoader.NodeInfo>();
				var allTreeInfo = m_TreeView.treeModel.root.children;
				foreach (var item in allTreeInfo)
				{
					var itemCasted = (MerinoTreeElement) item;
					var newNodeInfo = new YarnSpinnerLoader.NodeInfo();

					newNodeInfo.title = itemCasted.name;
					newNodeInfo.body = itemCasted.nodeBody;
					newNodeInfo.tags = itemCasted.nodeTags;
					var newPosition = new YarnSpinnerLoader.NodeInfo.Position();
					newPosition.x = itemCasted.nodePosition.x;
					newPosition.y = itemCasted.nodePosition.y;
					newNodeInfo.position = newPosition;

					nodeInfo.Add(newNodeInfo);
				}
				File.WriteAllText(AssetDatabase.GetAssetPath(currentFile), YarnSpinnerFileFormatConverter.ConvertNodesToYarnText(nodeInfo));
				EditorUtility.SetDirty(currentFile);
			}
		}

		/// <summary>
		/// Checks to see if TextAsset is a probably valid .yarn.txt file, or if it's just a random text file
		/// </summary>
		/// <param name="textAsset"></param>
		/// <returns></returns>
		bool IsProbablyYarnFile(TextAsset textAsset)
		{
			if ( AssetDatabase.GetAssetPath(textAsset).EndsWith(".yarn.txt") && textAsset.text.Contains("---") && textAsset.text.Contains("===") && textAsset.text.Contains("title:") )
			{
				return true;
			}
			else
			{
				return false;
			}
		}

		void AddNewNode()
		{
			var newID = m_TreeView.treeModel.GenerateUniqueID();
			var newNode = new MerinoTreeElement("NewNode" + newID.ToString(), 0, newID);
			newNode.nodeBody = "Write stuff here.";
			m_TreeView.treeModel.AddElement(newNode, m_TreeView.treeModel.root, 0);
			m_TreeView.FrameItem(newID);
			m_TreeView.SetSelection( new List<int>() {newID} );
			SaveDataToFile();
		}

		void OnSelectionChange ()
		{
			if (!m_Initialized)
				return;

			var possibleYarnFile = Selection.activeObject as TextAsset;
			if (possibleYarnFile != null && IsProbablyYarnFile(possibleYarnFile)) // possibleYarnFile != currentFile
			{
				currentFile = possibleYarnFile;
				m_TreeView.treeModel.SetData (GetData ());
				m_TreeView.Reload ();
			}
		}

		void OnGUI ()
		{
			InitIfNeeded();

			SearchBar (toolbarRect);
			DoTreeView (multiColumnTreeViewRect);

			if (viewState != null)
			{
				DrawSelectedNodes(nodeEditRect);
			}

		//	BottomToolBar (bottomToolbarRect);
		}

		void SearchBar (Rect rect)
		{
			treeView.searchString = m_SearchField.OnGUI (rect, treeView.searchString);
		}

		void DoTreeView (Rect rect)
		{
			float BUTTON_HEIGHT = 20;
			var buttonRect = rect;
			buttonRect.height = BUTTON_HEIGHT;
			if (GUI.Button(buttonRect, "+ NEW NODE"))
			{
				AddNewNode();
			}
			rect.y += BUTTON_HEIGHT;
			rect.height -= BUTTON_HEIGHT;
			m_TreeView.OnGUI(rect);
		}

		void DrawSelectedNodes(Rect rect)
		{
			GUILayout.BeginArea(rect);
			
			GUILayout.BeginHorizontal();
			if (currentFile != null)
			{
				useAutosave = EditorGUILayout.Toggle(useAutosave, GUILayout.Width(16));
				GUILayout.Label("AutoSave?   ", GUILayout.Width(0), GUILayout.ExpandWidth(true), GUILayout.MaxWidth(80) );
				GUI.enabled = !useAutosave;
				if (GUILayout.Button("Save", GUILayout.MaxWidth(60)))
				{
					SaveDataToFile();
					AssetDatabase.Refresh();
				}
				GUI.enabled = true;
			}
			if (GUILayout.Button("Save As...", GUILayout.MaxWidth(100)))
			{
				string defaultPath = Application.dataPath + "/";
				string defaultName = "NewYarnStory";
				if (currentFile != null)
				{
					defaultPath = Application.dataPath.Substring(0, Application.dataPath.Length - 6) + AssetDatabase.GetAssetPath(currentFile);
					defaultName = currentFile.name.Substring(0, currentFile.name.Length - 5);
				}
				string fullFilePath = EditorUtility.SaveFilePanel("Merino: save yarn.txt", Path.GetDirectoryName(defaultPath), defaultName, "yarn.txt");
				if (fullFilePath.Length > 0)
				{
					File.WriteAllText(fullFilePath, "");
					AssetDatabase.Refresh();
					currentFile = AssetDatabase.LoadAssetAtPath<TextAsset>( "Assets" + fullFilePath.Substring(Application.dataPath.Length));
					SaveDataToFile();
					AssetDatabase.Refresh();
				}
			}

			if (GUILayout.Button("New File", GUILayout.MaxWidth(100)))
			{
				currentFile = null;
				viewState = null;
				m_Initialized = false;
				InitIfNeeded();
			}
			GUILayout.Space(10);
			EditorGUILayout.SelectableLabel( currentFile != null ? AssetDatabase.GetAssetPath(currentFile) : "[no file loaded]", EditorStyles.whiteLargeLabel);
			GUILayout.EndHorizontal();

			if (viewState.selectedIDs.Count > 0)
			{
				scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
				
				foreach (var id in viewState.selectedIDs)
				{
					EditorGUI.BeginChangeCheck();
					
					string newName = EditorGUILayout.TextField(m_TreeView.treeModel.Find(id).name);
					string passage = m_TreeView.treeModel.Find(id).nodeBody;
					float height = EditorStyles.textArea.CalcHeight(new GUIContent(passage), rect.width);
					string newBody = GUILayout.TextArea(passage, GUILayout.Height(0f), GUILayout.ExpandHeight(true), GUILayout.MaxHeight(height));
					EditorGUILayout.Space();
					EditorGUILayout.Separator();
					
					// did user edit something?
					if (EditorGUI.EndChangeCheck())
					{
						// TODO: fix this somehow? I guess the file isn't writing quickly enough for Undo to catch it
						if (currentFile != null)
						{
							Undo.RecordObject(currentFile, "Merino: edit file ");
						}
						m_TreeView.treeModel.Find(id).name = newName;
						m_TreeView.treeModel.Find(id).nodeBody = newBody;
						if (currentFile != null && useAutosave)
						{
							SaveDataToFile();
						}
					}
				}

				EditorGUILayout.EndScrollView();
			}
			else
			{
				EditorGUILayout.HelpBox(" Select node(s) in sidebar.\n\n Left-Click: select\n Left-Click + Shift: select multiple\n Left-Click + Ctrl or Command: add / remove from selection", MessageType.Info);
			}

			GUILayout.EndArea();
		}

		void BottomToolBar (Rect rect)
		{
			GUILayout.BeginArea (rect);

			using (new EditorGUILayout.HorizontalScope ())
			{

				var style = "miniButton";
				if (GUILayout.Button("Expand All", style))
				{
					treeView.ExpandAll ();
				}

				if (GUILayout.Button("Collapse All", style))
				{
					treeView.CollapseAll ();
				}

				GUILayout.FlexibleSpace();

				GUILayout.Label (currentFile != null ? AssetDatabase.GetAssetPath (currentFile) : string.Empty);

				GUILayout.FlexibleSpace ();

				if (GUILayout.Button("Set sorting", style))
				{
					var myColumnHeader = (MyMultiColumnHeader)treeView.multiColumnHeader;
					myColumnHeader.SetSortingColumns (new int[] {4, 3, 2}, new[] {true, false, true});
					myColumnHeader.mode = MyMultiColumnHeader.Mode.LargeHeader;
				}


				GUILayout.Label ("Header: ", "minilabel");
				if (GUILayout.Button("Large", style))
				{
					var myColumnHeader = (MyMultiColumnHeader) treeView.multiColumnHeader;
					myColumnHeader.mode = MyMultiColumnHeader.Mode.LargeHeader;
				}
				if (GUILayout.Button("Default", style))
				{
					var myColumnHeader = (MyMultiColumnHeader)treeView.multiColumnHeader;
					myColumnHeader.mode = MyMultiColumnHeader.Mode.DefaultHeader;
				}
				if (GUILayout.Button("No sort", style))
				{
					var myColumnHeader = (MyMultiColumnHeader)treeView.multiColumnHeader;
					myColumnHeader.mode = MyMultiColumnHeader.Mode.MinimumHeaderWithoutSorting;
				}

				GUILayout.Space (10);
				
				if (GUILayout.Button("values <-> controls", style))
				{
					treeView.showControls = !treeView.showControls;
				}
			}

			GUILayout.EndArea();
		}
	}


	internal class MyMultiColumnHeader : MultiColumnHeader
	{
		Mode m_Mode;

		public enum Mode
		{
			LargeHeader,
			DefaultHeader,
			MinimumHeaderWithoutSorting
		}

		public MyMultiColumnHeader(MultiColumnHeaderState state)
			: base(state)
		{
			mode = Mode.DefaultHeader;
		}

		public Mode mode
		{
			get
			{
				return m_Mode;
			}
			set
			{
				m_Mode = value;
				switch (m_Mode)
				{
					case Mode.LargeHeader:
						canSort = true;
						height = 37f;
						break;
					case Mode.DefaultHeader:
						canSort = true;
						height = DefaultGUI.defaultHeight;
						break;
					case Mode.MinimumHeaderWithoutSorting:
						canSort = false;
						height = DefaultGUI.minimumHeight;
						break;
				}
			}
		}

		protected override void ColumnHeaderGUI (MultiColumnHeaderState.Column column, Rect headerRect, int columnIndex)
		{
			// Default column header gui
			base.ColumnHeaderGUI(column, headerRect, columnIndex);

			// Add additional info for large header
			if (mode == Mode.LargeHeader)
			{
				// Show example overlay stuff on some of the columns
				if (columnIndex > 2)
				{
					headerRect.xMax -= 3f;
					var oldAlignment = EditorStyles.largeLabel.alignment;
					EditorStyles.largeLabel.alignment = TextAnchor.UpperRight;
					GUI.Label(headerRect, 36 + columnIndex + "%", EditorStyles.largeLabel);
					EditorStyles.largeLabel.alignment = oldAlignment;
				}
			}
		}
	}

}
