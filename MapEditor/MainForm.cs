﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.IO;
using System.Reflection;
using System.Threading;

namespace MapEditor
{
	public delegate void DLogString(string message);
	public delegate CMap DCloneCurMap();
	public delegate bool DYesNoPrompt(string message, string caption);

    public partial class MainForm : Form
    {
		/// <summary>
		/// A local copy of the TileSets singleton instance.
		/// </summary>
		protected TileSets tileSets;

		/// <summary>
		/// The current map opened for editing.
		/// </summary>
		protected CMap curMap = null;

		/// <summary>
		/// If the map has unsaved changes.
		/// </summary>
		private bool _curMapDirty = false;
		protected bool curMapDirty
		{
			get { return _curMapDirty; }
			set
			{
				_curMapDirty = value;
				updateTitleBar();
				saveToolStripMenuItem.Enabled = value;
			}
		}

		/// <summary>
		/// The current layer being edited.
		/// </summary>
		protected int curLayer = 0;

		/// <summary>
		/// The tile to use as a brush when painting on the map.
		/// Only valid when the Tiles tool tab is selected.
		/// </summary>
		protected CTile curTile = null;

		/// <summary>
		/// The size of the current tile brush.
		/// </summary>
		protected int curBrushSize = 1;

		/// <summary>
		/// The current tab in the tools tab control.
		/// </summary>
		protected TabPage curToolsPage = null;

		/// <summary>
		/// The selected map entrance.
		/// Only valid when the Entrances tool tab is selected.
		/// </summary>
		private CMapEntrance _curEntrance = null;
		protected CMapEntrance curEntrance
		{
			get { return _curEntrance; }
			set { _curEntrance = value; updateCurEntrance(); }
		}

		/// <summary>
		/// The selected map exit.
		/// Only valid when the Exits tool tab is selected.
		/// </summary>
		private CMapExit _curExit = null;
		protected CMapExit curExit
		{
			get { return _curExit; }
			set { _curExit = value; updateCurExit(); }
		}

		/// <summary>
		/// Which layers are currently visible.
		/// </summary>
		protected bool[] layersVisible = new bool[] {true, true, true, true};

		/// <summary>
		/// Is the walkType layer visible.
		/// </summary>
		protected bool walkLayerVisible = true;

		/// <summary>
		/// Is the monster region layer visible.
		/// </summary>
		protected bool monsterRegionLayerVisible = true;

		/// <summary>
		/// Is the entrance and exit layer visible.
		/// </summary>
		protected bool entranceExitLayerVisible = true;

		/// <summary>
		/// Whether or not to draw the grid.
		/// </summary>
		protected bool drawGrid = false;

		/// <summary>
		/// The color of the grid.
		/// </summary>
		protected Color gridColor = Color.White;

		/// <summary>
		/// Tile images for walk types.
		/// </summary>
		protected CTile[] walkTypeTiles = new CTile[Enum.GetNames(typeof(EWalkType)).Length];

		/// <summary>
		/// Tile images for monster regions.
		/// </summary>
		protected CTile[] monsterRegionTiles;

		/// <summary>
		/// Worker thread for updating the minimap.
		/// </summary>
		protected Thread updateMiniMapThread;

		/// <summary>
		/// If the minimap needs to be updated.
		/// </summary>
		protected bool miniMapNeedsUpdate = false;

		#region Form Functions
		public MainForm()
		{
			InitializeComponent();
			curMapDirty = false;
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			try
			{
				// Set reference to the current selected tab
				curToolsPage = tabTools.SelectedTab;

				// Load tileSets
				tileSets = TileSets.instance;
				foreach (CTileSet ts in tileSets)
				{
					logString("Loaded tileset " + ts);
				}

				// Load walkType tiles
				string walkImagesFilename = Globals.tileDir + "walktypes.png";
				for (ushort i = 0; i < Enum.GetNames(typeof(EWalkType)).Length; i++)
				{
					string tileName = Enum.GetNames(typeof(EWalkType))[i];
					walkTypeTiles[i] = new CTile(i, tileName, walkImagesFilename, i, 0);
				}
				
				comboLayers.SelectedIndex = 0;
				comboBrushSize.SelectedIndex = 0;

				// Start worker thread for updating the minimap
				updateMiniMapThread = new Thread(new ThreadStart(updateMiniMapThreadFunc));
				updateMiniMapThread.Start();

				//openMap(Globals.mapDir + "overworld1.map");
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			// Cancel closing if map is dirty and user selects Cancel
			if (cancelActionIfDirty() == true)
				e.Cancel = true;

			// Stop the minimap worker thread
			updateMiniMapThread.Abort();
		}

		/// <summary>
		/// Updates the title bar with the map name, filename and whether the current map is dirty.
		/// </summary>
		private void updateTitleBar()
		{
			StringBuilder newTitle = new StringBuilder();
			newTitle.AppendFormat("MapEditor v{0}", Globals.version);

			if (curMap != null)
			{
				newTitle.AppendFormat(" - {0}", curMap.name);

				if (curMap.filename == "")
					newTitle.Append(" - unsaved map");
				else
					newTitle.AppendFormat(" - {0}", curMap.filename);

				if (curMapDirty == true)
					newTitle.Append(" *");
			}

			this.Text = newTitle.ToString();
		}

		/// <summary>
		/// Triggered when tools tab changes
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void tabTools_SelectedIndexChanged(object sender, EventArgs e)
		{
			curToolsPage = tabTools.SelectedTab;

			if (curToolsPage == tabTiles)
				tabTilesSelected();
			else if (curToolsPage == tabEntrances)
				tabEntrancesSelected();
			else if (curToolsPage == tabExits)
				tabExitsSelected();

			redrawMap();
		}

		/// <summary>
		/// Shows a Yes or No MessageBox with the provided text.
		/// </summary>
		/// <param name="message">The message to display.</param>
		/// <param name="caption">The caption of the MessgeBox.</param>
		/// <returns>true if user selected Yes, false if the user selected No.</returns>
		private bool yesNoPrompt(string message, string caption)
		{
			DialogResult res = MessageBox.Show(this, message, caption, MessageBoxButtons.YesNo);

			return res == DialogResult.Yes;
		}
		#endregion

		#region Menu Events

		/// <summary>
		/// Shows dialog to create a new map.
		/// Triggered when the File->New menu item is clicked.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void newToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (cancelActionIfDirty() == true)
				return;

			CNewMapForm dialog = new CNewMapForm();
			DialogResult res = dialog.ShowDialog();

			if (res == DialogResult.OK)
			{
				newMap(dialog.name, dialog.width, dialog.height, dialog.tileSet, dialog.monsterRegionGroup);
			}
		}

		/// <summary>
		/// Opens an existing map.
		/// Triggered when the File->Open menu item is clicked.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void openToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (cancelActionIfDirty() == true)
				return;

			OpenFileDialog fd = new OpenFileDialog();
			fd.Filter = "Map Files(*.map)|*.map";
			fd.InitialDirectory = Globals.mapDir;
			DialogResult res = fd.ShowDialog(this);

			if (res == DialogResult.OK)
			{
				openMap(fd.FileName);
			}
		}

		/// <summary>
		/// Saves the current map.
		/// Triggered when the File->Save menu item is clicked.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void saveToolStripMenuItem_Click(object sender, EventArgs e)
		{
			// Trigger saveAs if map has never been saved before.
			if (curMap.filename == "")
			{
				saveAsToolStripMenuItem.PerformClick();
				return;
			}

			saveMap(curMap.filename);
		}

		/// <summary>
		/// Saves the current map as a new file.
		/// Triggered when the File->SaveAs menu item is clicked.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			SaveFileDialog fd = new SaveFileDialog();
			fd.Filter = "Map Files(*.map)|*.map";
			fd.InitialDirectory = Globals.mapDir;
			DialogResult res = fd.ShowDialog(this);

			if (res == DialogResult.OK)
			{
				saveMap(fd.FileName);
			}
		}

		/// <summary>
		/// Exits the application.
		/// Triggered when the File->Exit menu item is clicked.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void exitToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Application.Exit();
		}

		/// <summary>
		/// Toggle layer 1 visibility.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void layer1ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			layer1ToolStripMenuItem.Checked = !layer1ToolStripMenuItem.Checked;
		}

		private void layer1ToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			layersVisible[0] = layer1ToolStripMenuItem.Checked;
			redrawMap();
		}

		/// <summary>
		/// Toggle layer 2 visibility.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void layer2ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			layer2ToolStripMenuItem.Checked = !layer2ToolStripMenuItem.Checked;
		}

		private void layer2ToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			layersVisible[1] = layer2ToolStripMenuItem.Checked;
			redrawMap();
		}

		/// <summary>
		/// Toggle layer 3 visibility.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void layer3ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			layer3ToolStripMenuItem.Checked = !layer3ToolStripMenuItem.Checked;
		}

		private void layer3ToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			layersVisible[2] = layer3ToolStripMenuItem.Checked;
			redrawMap();
		}

		/// <summary>
		/// Toggle layer 4 visibility.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void layer4ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			layer4ToolStripMenuItem.Checked = !layer4ToolStripMenuItem.Checked;
		}

		private void layer4ToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			layersVisible[3] = layer1ToolStripMenuItem.Checked;
			redrawMap();
		}

		/// <summary>
		/// Toggle walk layer visibility.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void walkLayerToolStripMenuItem_Click(object sender, EventArgs e)
		{
			walkLayerToolStripMenuItem.Checked = !walkLayerToolStripMenuItem.Checked;
		}
		
		private void walkLayerToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			walkLayerVisible = walkLayerToolStripMenuItem.Checked;
			redrawMap();
		}

		/// <summary>
		/// Toggle monster regions visibility.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void monsterRegionsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			monsterRegionsToolStripMenuItem.Checked = !monsterRegionsToolStripMenuItem.Checked;
		}

		private void monsterRegionsToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			monsterRegionLayerVisible = monsterRegionsToolStripMenuItem.Checked;
			redrawMap();
		}

		/// <summary>
		/// Toggle entrances and exits visibility.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void entrancesExitsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			entrancesExitsToolStripMenuItem.Checked = !entrancesExitsToolStripMenuItem.Checked;
		}

		private void entrancesExitsToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			entranceExitLayerVisible = entrancesExitsToolStripMenuItem.Checked;
			redrawMap();
		}

		/// <summary>
		/// Toggle grid visibility.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void gridToolStripMenuItem_Click(object sender, EventArgs e)
		{
			gridToolStripMenuItem.Checked = !gridToolStripMenuItem.Checked;
			drawGrid = gridToolStripMenuItem.Checked;

			redrawMap();
		}

		/// <summary>
		/// Displays a dialog to pick the grid color.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void gridColorToolStripMenuItem_Click(object sender, EventArgs e)
		{
			ColorDialog cd = new ColorDialog();
			cd.Color = gridColor;

			DialogResult res = cd.ShowDialog();
			if (res == DialogResult.OK)
			{
				gridColor = cd.Color;
				redrawMap();
			}
		}

		/// <summary>
		/// Displays the rename map dialog.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void renameToolStripMenuItem_Click(object sender, EventArgs e)
		{
			CRenameMapForm dialog = new CRenameMapForm(curMap.name);
			DialogResult res = dialog.ShowDialog();

			if (res == DialogResult.OK)
			{
				curMap.name = dialog.name;
				curMapDirty = true;
			}
		}

		/// <summary>
		/// Displays the resize map dialog.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void resizeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			CResizeMapForm dialog = new CResizeMapForm(curMap);
			dialog.ShowDialog(this);
		}
		#endregion

		#region Tile Palette Functions
		/// <summary>
		/// Called when the tiles tab is selected.
		/// </summary>
		private void tabTilesSelected()
		{
			// Set brush size
			comboBrushSize_SelectedIndexChanged(comboBrushSize, EventArgs.Empty);

			// Deselect any entrances or exits
			curEntrance = null;
			curExit = null;
		}

		/// <summary>
		/// Redraws the tile palette with the tiles from the current layer. 
		/// Triggered when the layer dropdown is changed.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void comboLayers_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (curMap == null)
				return;

			try
			{
				int i = 0;

				curLayer = comboLayers.SelectedIndex;

				// Enable visibility for the selected layer
				switch (curLayer)
				{
					case 0:
						layer1ToolStripMenuItem.Checked = true;
						break;
					case 1:
						layer2ToolStripMenuItem.Checked = true;
						break;
					case 2:
						layer3ToolStripMenuItem.Checked = true;
						break;
					case 3:
						layer4ToolStripMenuItem.Checked = true;
						break;
					case 4:
						walkLayerToolStripMenuItem.Checked = true;
						break;
					case 5:
						monsterRegionsToolStripMenuItem.Checked = true;
						break;
				}

				// Remove all tiles from previous layer
				panelTiles.Controls.Clear();	

				// Add tiles from current layer
				if (curLayer < Globals.layerCount)
				{
					foreach (CTileSetGroup group in curMap.tileSet.layers[curLayer].groups)
					{
						foreach (CTile tile in group.tiles)
						{
							PictureBox pic = createPictureBoxFromTile(tile, i);
							panelTiles.Controls.Add(pic);
							i++;
						}
					}
				}
				else if (curLayer == Globals.layerCount)
				{
					foreach (CTile tile in walkTypeTiles)
					{
						PictureBox pic = createPictureBoxFromTile(tile, i);
						panelTiles.Controls.Add(pic);
						i++;
					}
				}
				else if (curLayer == Globals.layerCount + 1)
				{
					foreach (CTile tile in monsterRegionTiles)
					{
						PictureBox pic = createPictureBoxFromTile(tile, i);
						panelTiles.Controls.Add(pic);
						i++;
					}
				}

				// Select the first tile when changing layers
				tile_Click(panelTiles.Controls[0], EventArgs.Empty);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
		}

		/// <summary>
		/// Creates a PictureBox for the tile palette.
		/// </summary>
		/// <param name="tile">The tile to create from.</param>
		/// <param name="index">The index used for positioning.</param>
		/// <returns>The PictureBox for the tile.</returns>
		private PictureBox createPictureBoxFromTile(CTile tile, int index)
		{
			try
			{
				PictureBox pic = new PictureBox();
				pic.Image = tile.image;
				pic.Width = Globals.tileSize;
				pic.Height = Globals.tileSize;
				pic.Location = new Point((index % 5) * Globals.tileSize, (index / 5) * Globals.tileSize);
				pic.Click += new System.EventHandler(this.tile_Click);
				pic.Tag = tile;

				return pic;
			}
			catch { throw; }
		}

		/// <summary>
		/// Sets the selected tile as the current brush.
		/// Triggered when a tile in the tile palette is clicked. 
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void tile_Click(object sender, EventArgs e)
		{
			PictureBox curPic = (PictureBox)sender;
			curTile = (CTile)curPic.Tag;

			foreach (PictureBox pic in panelTiles.Controls)
			{
				if (pic == curPic)
				{
					Bitmap img = (Bitmap)((CTile)pic.Tag).image.Clone();
					Graphics graphics = Graphics.FromImage(img);
					Pen pen = new Pen(Color.Red, 2);

					// Draw border around current tile
					graphics.DrawRectangle(pen, 0, 0, Globals.tileSize, Globals.tileSize);
					pic.Image = img;
				}
				else
				{
					// Redraw image to remove border
					pic.Image = ((CTile)pic.Tag).image;
				}
			}
			GC.Collect();
        }

		/// <summary>
		/// Sets the brush size.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void comboBrushSize_SelectedIndexChanged(object sender, EventArgs e)
		{
			curBrushSize = (comboBrushSize.SelectedIndex * 2) + 1;
		}

		/// <summary>
		/// Process map clicks while Tiles tool tab is selected.
		/// Paints the selected tile onto the map in the selected layer.
		/// Called from panelMap_MouseClick().
		/// </summary>
		/// <param name="x">The tile X location of the click.</param>
		/// <param name="y">The tile Y location of the click.</param>
		/// <param name="layer">The layer to change.</param>
		/// <param name="tileId">The id of the current tile.</param>
		private void tileMapClicked(int x, int y, int layer, ushort tileId)
		{
			// Get cell at click location
			CMapCell cell = curMap.cells[x, y];

			// Update tile or walkType based on layer
			if (layer < Globals.layerCount)
				cell.tiles[layer] = tileId;
			else if (layer == Globals.layerCount)
				cell.walkType = (EWalkType)tileId;
			else if (layer == Globals.layerCount + 1)
				cell.monsterRegionId = tileId;

			curMapDirty = true;
		}
		#endregion

		#region Map Entrances Function
		/// <summary>
		/// Called when the entrances tab is selected.
		/// </summary>
		private void tabEntrancesSelected()
		{
			// Set brush size
			curBrushSize = 1;

			// Deselect any tiles or exits
			//curTile = null;
			curExit = null;
		}
		
		/// <summary>
		/// Process map clicks while Entrances tool tab is selected.
		/// Clicking a tile with an entance selected that entrance.
		/// Clicking a tile without an entrance creates a new entrance.
		/// Called from panelMap_MouseClick().
		/// </summary>
		/// <param name="x">The tile X location of the click.</param>
		/// <param name="y">The tile Y location of the click.</param>
		private void entranceMapClicked(int x, int y)
		{
			// Get entrance at click location
			CMapEntrance entrance = curMap.getEntranceAt(x, y);

			if (entrance != null)
			{
				logString(string.Format("Selected Entrance {0}", entrance.id));
				curEntrance = entrance;
			}
			else
			{
				int newId = 0;

				// Get the next unused entrance id
				if (curMap.entrances.Count > 0)
					newId = curMap.entrances.Keys.Max() + 1;

				// Create new entrance
				CMapEntrance newEntrance = new CMapEntrance(newId, x, y);
				curMap.entrances.Add(newId, newEntrance);

				logString(string.Format("Created new Entrance {0} at {1},{2}", newEntrance.id, x, y));

				curEntrance = newEntrance;

				curMapDirty = true;
			}
		}

		/// <summary>
		/// Called from the setter for curEntrance.
		/// </summary>
		private void updateCurEntrance()
		{
			if (curEntrance == null)
			{
				numericEntranceId.Enabled = false;
				buttonUpdateEntrance.Enabled = false;
				buttonDeleteEntrance.Enabled = false;
			}
			else
			{
				numericEntranceId.Enabled = true;
				buttonUpdateEntrance.Enabled = true;
				buttonDeleteEntrance.Enabled = true;

				numericEntranceId.Value = curEntrance.id;
			}
		}

		/// <summary>
		/// Updates the Id of an entrance.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void buttonUpdateEntrance_Click(object sender, EventArgs e)
		{
			int newId = (int)numericEntranceId.Value;

			if (curMap.entrances.ContainsKey(newId))
			{
				MessageBox.Show(string.Format("There is already an entrance with an id of {0}", newId));
				return;
			}

			curMap.entrances.Remove(curEntrance.id);
			curEntrance.id = newId;
			curMap.entrances.Add(newId, curEntrance);

			curMapDirty = true;

			redrawMap();
		}

		/// <summary>
		/// Deletes an entrance.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void buttonDeleteEntrance_Click(object sender, EventArgs e)
		{
			if (yesNoPrompt("Do you want to delete this entrance?", "Delete Entrance"))
			{
				curMap.entrances.Remove(curEntrance.id);

				curMapDirty = true;

				redrawMap();
			}
		}
		#endregion

		#region Map Exits Function
		/// <summary>
		/// Called when the exits tab is selected.
		/// </summary>
		private void tabExitsSelected()
		{
			// Set brush size
			curBrushSize = 1;

			// Deselect any tiles or entrances
			//curTile = null;
			curEntrance = null;
		}

		/// <summary>
		/// Process map clicks while Exits tool tab is selected.
		/// Clicking a tile with an exit selected that exit.
		/// Clicking a tile without an exit creates a new exit.
		/// Called from panelMap_MouseClick().
		/// </summary>
		/// <param name="x">The tile X location of the click.</param>
		/// <param name="y">The tile Y location of the click.</param>
		private void exitMapClicked(int x, int y)
		{
			// Get exit at click location
			CMapExit exit = curMap.getExitAt(x, y);

			if (exit != null)
			{
				logString(string.Format("Selected Exit at {0},{1}", x, y));
				curExit = exit;
			}
			else
			{
				// Create new exit
				CMapExit newExit = new CMapExit(new Guid(), 0, x, y);
				curMap.exits.Add(newExit);

				logString(string.Format("Created new Exit at {0},{1}", x, y));

				curExit = newExit;

				curMapDirty = true;
			}
		}

		/// <summary>
		/// Called from the setter for curExit.
		/// </summary>
		private void updateCurExit()
		{
			if (curExit == null)
			{
				comboExitMapName.Enabled = false;
				numericExitEntranceId.Enabled = false;
				buttonUpdateExit.Enabled = false;
				buttonPreviewExit.Enabled = false;
				buttonDeleteExit.Enabled = false;
			}
			else
			{
				comboExitMapName.Enabled = true;
				numericExitEntranceId.Enabled = true;
				buttonUpdateExit.Enabled = true;
				buttonPreviewExit.Enabled = true;
				buttonDeleteExit.Enabled = true;

				comboExitMapName.DataSource = MapList.mapNames;
				comboExitMapName.SelectedValue = curExit.mapUuid;

				numericExitEntranceId.Value = curExit.mapEntranceId;
			}
		}

		private void buttonUpdateExit_Click(object sender, EventArgs e)
		{
			int newEntranceId = (int)numericExitEntranceId.Value;
			Guid newMapUuid = (Guid)comboExitMapName.SelectedValue;
			string newMapName = ((Tuple<Guid, string>)comboExitMapName.SelectedItem).Item2;

			// TODO: Check that map entrance exists
			/*if ()
			{
				MessageBox.Show(string.Format("There is exit with an id of {0} in map {1}", newEntranceId, newMapName));
				return;
			}*/

			curExit.mapEntranceId = newEntranceId;
			curExit.mapUuid = (Guid)comboExitMapName.SelectedValue;

			curMapDirty = true;

			redrawMap();
		}

		private void buttonPreviewExit_Click(object sender, EventArgs e)
		{
			CMapExitPreviewForm dialog = new CMapExitPreviewForm(curExit);

			dialog.Show(this);
		}

		private void buttonDeleteExit_Click(object sender, EventArgs e)
		{
			if (yesNoPrompt("Do you want to delete this exit?", "Delete Exit"))
			{
				curMap.exits.Remove(curExit);

				curMapDirty = true;

				redrawMap();
			}
		}

		#endregion

		#region Scroll Bar Functions
		/// <summary>
		/// Resets the scrollbars after a new map is created or a map is loaded.
		/// </summary>
		private void resetScrollBars()
		{
			scrollMapH.Value = 0;
			scrollMapH.Minimum = 0;
			scrollMapH.SmallChange = 1;

			scrollMapV.Value = 0;
			scrollMapV.Minimum = 0;
			scrollMapV.SmallChange = 1;

			// Set Maximum and LargeChange
			setScrollBarSizes();
		}

		/// <summary>
		/// Set the scrolbar sizes based on map size and window size.
		/// Called when a map is created or loaded or when the window is resized.
		/// The formula for Maximum comes from http://msdn.microsoft.com/en-us/library/system.windows.forms.scrollbar.maximum.aspx			
		/// It needs tweaking for maximum value.
		/// </summary>
		private void setScrollBarSizes()
		{
			// Bail if there is no current map
			if (curMap == null)
				return;

			// Calculate how many tiles are displayable in the panel
			int panelTileWidth = panelMap.Size.Width / Globals.tileSize;
			int panelTileHeight = panelMap.Size.Height / Globals.tileSize;

			// Enable the scrollbar
			scrollMapH.Enabled = true;
			// Move over one full screen, minus one tile
			scrollMapH.LargeChange = panelTileWidth - 1;
			// Needs adjusting, scrolls too far past the end
			scrollMapH.Maximum = Math.Max(0, curMap.width - panelTileWidth + scrollMapH.LargeChange);

			// Enable the scrollbar
			scrollMapV.Enabled = true;
			// Move over one full screen, minus one tile
			scrollMapV.LargeChange = panelTileHeight - 1;
			// Needs adjusting, scrolls too far past the end
			scrollMapV.Maximum = Math.Max(0, curMap.height - panelTileHeight + scrollMapV.LargeChange);
		}

		/// <summary>
		/// Event called when the horizontal scrollbar is moved.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void scrollMapH_Scroll(object sender, ScrollEventArgs e)
		{
			// Redraw map
			redrawMap();
			
			//logString(string.Format("h={0}", ((HScrollBar)sender).Value));
		}

		/// <summary>
		/// Event called when the vertical scrollbar is moved.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void scrollMapV_Scroll(object sender, ScrollEventArgs e)
		{
			// Redraw map
			redrawMap();
			
			//logString(string.Format("v={0}", ((VScrollBar)sender).Value));
		}
		#endregion

		#region Map Functions
		/// <summary>
		/// Creates and displays a new map.
		/// </summary>
		/// <param name="name">Map name.</param>
		/// <param name="width">Map width in tiles.</param>
		/// <param name="height">Map height in tiles.</param>
		/// <param name="tileSet">Map tileset.</param>
		private void newMap(string name, int width, int height, CTileSet tileSet, CMonsterRegionGroup regionGroup)
		{
			curMap = new CMap(name, width, height, tileSet, regionGroup);
			curMapDirty = true;
			
			saveToolStripMenuItem.Enabled = true;
			saveAsToolStripMenuItem.Enabled = true;
			
			displayCurrentMap();
		}

		/// <summary>
		/// Opens a map from a file.
		/// </summary>
		/// <param name="filename">Filename of map to open.</param>
		private void openMap(string filename)
		{
			try
			{
				curMap = new CMap(filename);
				curMapDirty = false;
				
				saveAsToolStripMenuItem.Enabled = true;
				
				displayCurrentMap();
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
		}

		/// <summary>
		/// Saves the current map to a file.
		/// </summary>
		/// <param name="filename">Filename to save map to.</param>
		private void saveMap(string filename)
		{
			try
			{
				curMap.save(filename, new DYesNoPrompt(yesNoPrompt));
				curMapDirty = false;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
		}

		/// <summary>
		/// Checks if the current map is dirty and prompts to save it before continuing.
		/// Saves map if user selects Yes.
		/// </summary>
		/// <returns>False if map isn't dirty or user selected Yes or No. True if user selected Cancel.</returns>
		private bool cancelActionIfDirty()
		{
			if (curMapDirty == false)
				return false;

			DialogResult Res = MessageBox.Show("The map has unsaved changes. Do you want to save the map?", "", 
				MessageBoxButtons.YesNoCancel);

			if (Res == DialogResult.Yes)
			{
				saveToolStripMenuItem.PerformClick();

				// User cancelled out of saving a new map
				if (curMap.filename == "")
					return true;

				curMapDirty = false;

				return false;
			}
			else if (Res == DialogResult.No)
				return false;
			else //if (Res == DialogResult.Cancel)
				return true;
		}

		/// <summary>
		/// Starts displaying the current map.
		/// </summary>
		private void displayCurrentMap()
		{
			// Enable layer dropdown
			comboLayers.Enabled = true;

			// Enable brush size dropdown
			comboBrushSize.Enabled = true;

			// Enable Map menu item
			mapToolStripMenuItem.Enabled = true;

			// Reset layer palette. If current layer is 0, trigger change event manually because setting the index 
			// to itself doesn't trigger a change event.
			if (curLayer == 0)
				comboLayers_SelectedIndexChanged(comboLayers, EventArgs.Empty);
			else
				comboLayers.SelectedIndex = 0;

			// Load monster region tiles
			const int maxMonsterRegions = 13;
			string monsterRegionImagesFilename = "monsterregions.png";
			int monsterRegionCount = curMap.monsterRegionGroup.monsterRegions.Length;
			CMonsterRegionGroup regionGroup = curMap.monsterRegionGroup;

			if (monsterRegionCount > maxMonsterRegions)
				throw new Exception(string.Format("There aren't enough colors set for monster group {0}. There are only {1} colors set.",
						regionGroup.name, maxMonsterRegions));

			monsterRegionTiles = new CTile[monsterRegionCount];
			for (ushort i = 0; i < monsterRegionCount; i++)
			{
				CMonsterRegion region = regionGroup.monsterRegions[i];
				string tileName = region.name;
				monsterRegionTiles[i] = new CTile(i, tileName, monsterRegionImagesFilename, i, 0);
			}

			// Redraw the map
			redrawMap();

			// Reset scrollbar values
			resetScrollBars();

			// Reset selected entrance and exit
			curEntrance = null;
			curExit = null;
		}

		private void redrawMap(bool redrawMiniMap = true)
		{
			if (curMap == null)
				return;

			panelMap.Refresh();

			if (redrawMiniMap == true)
			{
				// Tell the minimap update thread there is a change waiting
				miniMapNeedsUpdate = true;
			}
		}

		/// <summary>
		/// Paints the map on the panel.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void panelMap_Paint(object sender, PaintEventArgs e)
		{
			// Local var to save some typing
			int tileSize = Globals.tileSize;

			// Create an image to paint on. Painting is double buffered.
			Bitmap buffer = new Bitmap(panelMap.Size.Width, panelMap.Size.Height);
			Graphics bufferGraphics = Graphics.FromImage(buffer);
			bufferGraphics.Clear(System.Drawing.SystemColors.Control);

			if (curMap != null)
			{
				// Number of tiles displayable inside the panel
				int panelTilesX = (panelMap.Size.Width / tileSize) + 1;
				int panelTilesY = (panelMap.Size.Height / tileSize) + 1;

				// Starting offset, in tiles, in the map to start painting from
				int offsetX = scrollMapH.Value;
				int offsetY = scrollMapV.Value;
				
				// The last tile to paint on screen
				int endX = Math.Min(curMap.width - offsetX, panelTilesX);
				int endY = Math.Min(curMap.height - offsetY, panelTilesY);

				// Pen for drawing lines and squares
				Pen pen = new Pen(gridColor);

				// Draw map tiles
				for (int x = 0; x < endX; x++)
				{
					for (int y = 0; y < endY; y++)
					{
						for (int z = 0; z < Globals.layerCount; z++)
						{
							// Skip this layer if its visibility is turned off
							if (!layersVisible[z])
								continue;

							// Get the tile id to paint
							ushort tileId = curMap.cells[x + offsetX, y + offsetY].tiles[z];
							
							// Skip drawing if the tile is fully transparent
							if (tileId == 0)
								continue;

							// Get the tile to paint
							CTile tile = curMap.tileSet.layers[z].getTileFromId(tileId);

							// Paint tile onto buffer
							bufferGraphics.DrawImage(tile.image, x * tileSize, y * tileSize, tileSize, tileSize);
						}

						// Draw the walk layer if it is visible
						if (walkLayerVisible)
						{
							// Get the tile id to paint
							ushort walkTileId = (ushort)curMap.cells[x + offsetX, y + offsetY].walkType;

							// Skip drawing if the tile is fully transparent
							if ((EWalkType)walkTileId != EWalkType.NormalWalk)
							{
								// Get the tile to paint
								CTile walkTile = walkTypeTiles[walkTileId];

								// Paint tile onto buffer
								bufferGraphics.DrawImage(walkTile.image, x * tileSize, y * tileSize, tileSize, tileSize);
							}
						}

						// Draw the monster region layer if it is visible
						if (monsterRegionLayerVisible)
						{
							// Get the tile id to paint
							ushort regionTileId = (ushort)curMap.cells[x + offsetX, y + offsetY].monsterRegionId;

							// Skip drawing if the tile is fully transparent
							if (regionTileId != 0)
							{
								// Get the tile to paint
								CTile regionTile = monsterRegionTiles[regionTileId];

								// Paint tile onto buffer
								bufferGraphics.DrawImage(regionTile.image, x * tileSize, y * tileSize, tileSize, tileSize);
							}
						}

						if (drawGrid)
							bufferGraphics.DrawLine(pen, 0, y * tileSize, endX * tileSize, y * tileSize);
					}

					if (drawGrid)
						bufferGraphics.DrawLine(pen, x * tileSize, 0, x * tileSize, endY * tileSize); 
				}

				// Draw entrance and exit squares
				if (entranceExitLayerVisible)
				{
					//pen = new Pen(Color.Aqua);
					foreach (int i in curMap.entrances.Keys)
					{
						CMapEntrance ent = curMap.entrances[i];

						// Skip if tile is off screen
						if (ent.tileX < offsetX || ent.tileX >= endX || ent.tileY < offsetY || ent.tileY >= endY)
							continue;

						if (ent == curEntrance)
							pen = new Pen(Color.Aqua, 3);
						else
							pen = new Pen(Color.Aqua, 1);

						Rectangle rect = new Rectangle((ent.tileX - offsetX) * tileSize, (ent.tileY - offsetY) * tileSize,
							tileSize, tileSize);

						// Draw entrance
						bufferGraphics.DrawRectangle(pen, rect);

						Font font = new Font("Arial", 10);
						SolidBrush brush = new SolidBrush(Color.Aqua);

						bufferGraphics.DrawString(i.ToString(), font, brush, rect);

						// Highlight the selected entrance
						if (ent == curEntrance)
							bufferGraphics.DrawEllipse(pen, rect.X + (rect.Width / 4), rect.Y + (rect.Height / 4), 
								rect.Width / 2, rect.Height / 2);
					}

					//pen = new Pen(Color.Blue);
					foreach (CMapExit exit in curMap.exits)
					{
						// Skip if tile is off screen
						if (exit.tileX < offsetX || exit.tileX >= endX || exit.tileY < offsetY || exit.tileY >= endY)
							continue;

						if (exit == curExit)
							pen = new Pen(Color.Blue, 3);
						else
							pen = new Pen(Color.Blue, 1);

						Rectangle rect = new Rectangle((exit.tileX - offsetX) * tileSize, (exit.tileY - offsetY) * tileSize,
							tileSize, tileSize);

						// Draw exit
						bufferGraphics.DrawRectangle(pen, rect);

						// Highlight the selected entrance
						if (exit == curExit)
							bufferGraphics.DrawEllipse(pen, rect.X + (rect.Width / 4), rect.Y + (rect.Height / 4), 
								rect.Width / 2, rect.Height / 2);
					}
				}

				pen.Dispose();
			}

			bufferGraphics.Dispose();
			
			// Copy image from buffer to panel
			e.Graphics.DrawImage(buffer, 0, 0, panelMap.Size.Width, panelMap.Size.Height);
		}

		/// <summary>
		/// Event called when window size changes.
		/// Reset scrollbar sizes.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void panelMap_SizeChanged(object sender, EventArgs e)
		{
			setScrollBarSizes();
		}

		/// <summary>
		/// Event called when the mouse moves over the map.
		/// Updates the coordinates in the status bar.
		/// If left mouse button is down, trigger a mouse click event on the cell.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void panelMap_MouseMove(object sender, MouseEventArgs e)
		{
			// The tile position of the mouse, relative to the start of the map
			int tileX = (e.X / Globals.tileSize) + scrollMapH.Value;
			int tileY = (e.Y / Globals.tileSize) + scrollMapV.Value;

			// The tile position of the start of the brush, relative to the start of the panel
			int brushStartX = (e.X / Globals.tileSize) - (curBrushSize / 2);
			int brushStartY = (e.Y / Globals.tileSize) - (curBrushSize / 2);

			Graphics panelGraphics = panelMap.CreateGraphics();
			Pen pen = new Pen(Color.Red, 2);

			// Redraw map to remove old brush outline, but don't update minimap
			redrawMap(false);

			if (curMap != null && tileX < curMap.width && tileY < curMap.height)
			{
				// Draw a rectangle around the current brush
				panelGraphics.DrawRectangle(pen, brushStartX * Globals.tileSize, brushStartY * Globals.tileSize,
					curBrushSize * Globals.tileSize, curBrushSize * Globals.tileSize);

				// Update status bar with current map coordinates
				textStatus.Text = string.Format("{0}, {1}", tileX, tileY);

				// If left mouse button down, trigger click on current cell
				if (e.Button == MouseButtons.Left)
					panelMap_MouseClick(panelMap, e);
			}
			else
				textStatus.Text = "";
		}

		/// <summary>
		/// Event called when clicking on a cell.
		/// Changes the tile in the current layer of the cell with the current brush tile.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void panelMap_MouseClick(object sender, MouseEventArgs e)
		{
			// The tile position of the mouse, relative to the start of the map
			int tileX = (e.X / Globals.tileSize) + scrollMapH.Value;
			int tileY = (e.Y / Globals.tileSize) + scrollMapV.Value;

			// The layer to operate on
			int curLayer = comboLayers.SelectedIndex;

			// Calculate the tiles contained within the current brush size
			int brushStartX = tileX - (curBrushSize / 2);
			int brushEndX = tileX + (curBrushSize / 2);
			int brushStartY = tileY - (curBrushSize / 2);
			int brushEndY = tileY + (curBrushSize / 2);

			// Bail if there is no map, or the mouse is located off the map
			if (curMap == null || tileX >= curMap.width || tileY >= curMap.height)
				return;

			// Update all entities within the current brush
			for (int x = brushStartX; x <= brushEndX; x++)
			{
				for (int y = brushStartY; y <= brushEndY; y++)
				{
					// Only draw if the coordinates are within the map
					if (x >= 0 && y >= 0 && x < curMap.width && y < curMap.height)
					{
						if (curToolsPage == tabTiles)
						{
							tileMapClicked(x, y, curLayer, curTile.id);
						}
						else if (curToolsPage == tabEntrances)
						{
							entranceMapClicked(x, y);
						}
						else if (curToolsPage == tabExits)
						{
							exitMapClicked(x, y);
						}
					}
				}
			}

			redrawMap();
		}
		#endregion

		#region MiniMap Functions
		/// <summary>
		/// Clones the current map.
		/// Needs to be called on the main thread through an Invoke() call.
		/// </summary>
		/// <returns>A clone of the current map</returns>
		private CMap cloneCurMap()
		{
			return (CMap)curMap.Clone();
		}

		/// <summary>
		/// Background thread function to update the minimap.
		/// Only runs if there is a copy of the map in the updateMiniMapQueue.
		/// This implementation is probably not the best way to do it, but I'm just
		/// interested in getting things up and running quickly for now.
		/// </summary>
		private void updateMiniMapThreadFunc()
		{
			int i = 0;

			while (true)
			{
				if (miniMapNeedsUpdate == true)
				{
					miniMapNeedsUpdate = false;

					Console.WriteLine("{0} {1}", i++, DateTime.Now);

					// Copy the current map on the main thread
					CMap curMapCopy = (CMap)this.Invoke(new DCloneCurMap(cloneCurMap));

					// Update the minimap
					updateMiniMap(curMapCopy);
				}
				else
					Thread.Sleep(100);
			}
		}

		/// <summary>
		/// Updates the minimap image.
		/// This is horribly inefficient right now.
		/// </summary>
		private void updateMiniMap(CMap curMapCopy)
		{
			try
			{
				// Draw blank image if map is null
				if (curMapCopy == null)
				{
					Console.WriteLine("Updating minimap without a map");
					Bitmap bmp = new Bitmap(picMiniMap.Size.Width, picMiniMap.Size.Height);
					Graphics bmpGraphics = Graphics.FromImage(bmp);
					bmpGraphics.Clear(System.Drawing.SystemColors.Control);

					this.Invoke((MethodInvoker)delegate
					{
						picMiniMap.Image = bmp;
					});

					return;
				}

				// Local var to save some typing
				int tileSize = Globals.tileSize;

				// Get pixel dimensions of full map
				int mapWidth = curMapCopy.width * tileSize;
				int mapHeight = curMapCopy.height * tileSize;

				// Create a full sized image to paint on
				Bitmap buffer = new Bitmap(mapWidth, mapHeight);
				Graphics bufferGraphics = Graphics.FromImage(buffer);
				bufferGraphics.Clear(System.Drawing.SystemColors.Control);

				for (int x = 0; x < curMapCopy.width; x++)
				{
					for (int y = 0; y < curMapCopy.height; y++)
					{
						for (int z = 0; z < Globals.layerCount; z++)
						{
							// Skip this layer if its visibility is turned off
							if (!layersVisible[z])
								continue;

							// Get the tile id to paint
							ushort tileId = curMapCopy.cells[x, y].tiles[z];

							// Skip drawing if the tile is fully transparent
							if (tileId == 0)
								continue;

							// Get the tile to paint
							CTile tile = curMapCopy.tileSet.layers[z].getTileFromId(tileId);
							Bitmap tileImage = tile.image;

							// Paint tile onto buffer
							bufferGraphics.DrawImage(tileImage, x * tileSize, y * tileSize, tileSize, tileSize);
						}
					}
				}

				int newWidth = 0;
				int newHeight = 0;

				// Calculate the scaled down size of the map within the minimap image
				if (mapWidth > mapHeight)
				{
					newWidth = picMiniMap.Size.Width;
					newHeight = (int)Math.Floor(((double)mapHeight / mapWidth) * picMiniMap.Size.Width);
				}
				else
				{
					newHeight = picMiniMap.Size.Height;
					newWidth = (int)Math.Floor(((double)mapWidth / mapHeight) * picMiniMap.Size.Height);
				}

				// Create blank image that is the same size as the minimap
				Bitmap newBmp = new Bitmap(picMiniMap.Size.Width, picMiniMap.Size.Height);
				Graphics newBmpGraphics = Graphics.FromImage(newBmp);
				newBmpGraphics.Clear(System.Drawing.SystemColors.Control);

				// Draw map centered on newBmp
				newBmpGraphics.DrawImage(buffer, (picMiniMap.Size.Width / 2) - (newWidth / 2), 
					(picMiniMap.Size.Height / 2) - (newHeight / 2), newWidth, newHeight);

				// Update minimp
				this.Invoke((MethodInvoker)delegate
				{
					picMiniMap.Image = newBmp;
				});


			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
			finally
			{
				GC.SuppressFinalize(this);
				GC.Collect();
			}
		}
		#endregion

		public void logString(string message)
		{
			if (!message.EndsWith("\n"))
				message += "\n";

			txtLog.AppendText(message);
		}
    }
}
