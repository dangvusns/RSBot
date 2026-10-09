using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Client;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Extensions;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;
using RSBot.Map.Renderer;
using RSBot.NavMeshApi.Dungeon;
using SDUI.Controls;
using Region = RSBot.Core.Objects.Region;

namespace RSBot.Map.Views;

[ToolboxItem(false)]
public partial class Main : DoubleBufferedControl
{
    private readonly UiEventSubscriptions _uiEvents;

    /// <summary>
    ///     The grid size
    /// </summary>
    private const int GridSize = 3;
    // These fixed map colors are cached for the process lifetime and used only on the UI thread.
    private static readonly Dictionary<int, SolidBrush> _circleBrushes = new();
    private static readonly Dictionary<int, Pen> _circlePens = new();
    private static readonly Pen _destinationPen = new(Color.BlanchedAlmond, 1) { DashStyle = DashStyle.Dot };

    /// <summary>
    ///     The Sector Image Size
    /// </summary>
    private const int SectorSize = 256;

    /// <summary>
    ///     The cached Images
    /// </summary>
    private readonly Dictionary<string, Image> _cachedImages;

    /// <summary>
    ///     The current sector graphic
    /// </summary>
    private Image _currentSectorGraphic;

    /// <summary>
    ///     The X Sector identifier
    /// </summary>
    private byte _currentXSec;

    /// <summary>
    ///     The Y Sector identifier
    /// </summary>
    private byte _currentYSec;

    private ushort? _currentRegion;
    private string _currentFloorName;

    /// <summary>
    ///     The map points
    /// </summary>
    private Image[] _mapEntityImages;

    /// <summary>
    ///     The Zoom identifier
    /// </summary>
    private readonly float _scale = SectorSize / 192.0f;

    /// <summary>
    ///     <inheritdoc />
    /// </summary>
    private BufferedGraphics bufferedGraphics;

    /// <summary>
    ///     <inheritdoc />
    /// </summary>
    private readonly BufferedGraphicsContext bufferedGraphicsContext;

    private readonly NavMeshRenderer _navMeshRenderer;

    /// <summary>
    ///     The graphics the buffer is rendered to; replaced (and disposed) when the canvas is resized.
    /// </summary>
    private Graphics _canvasGraphics;

    /// <summary>
    ///     The rows collected while drawing a frame; the list is only updated once a second.
    /// </summary>
    private readonly List<(string Key, string Name, string Type, byte Level, Position Position)> _gridRows = new();

    private DateTime _lastGridUpdate = DateTime.MinValue;

    /// <summary>
    ///     The player arrow rotated for <see cref="_playerArrowAngle" />, rebuilt when the player turns.
    /// </summary>
    private Bitmap _playerArrow;

    private int _playerArrowAngle = int.MinValue;

    private Font _boldFont;

    /// <summary>
    /// The region name
    /// </summary>
    private string _regionName = string.Empty;

    /// <summary>
    ///     Auxiliary variable to auto select unique
    /// </summary>
    private bool _autoSelectUnqiue = false;

    /// <summary>
    ///     Auxiliary variable, so sound is only played once if unique is spotted.
    /// </summary>
    private bool _isUniqueSpotted = false;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Main" /> class.
    /// </summary>
    public Main()
    {
        InitializeComponent();
        _uiEvents = new UiEventSubscriptions(this);
        if (DesignMode)
            return;

        _cachedImages ??= new();

        _uiEvents.Subscribe("OnEnterGame", OnEnterGame);

        bufferedGraphicsContext = BufferedGraphicsManager.Current;
        bufferedGraphicsContext.MaximumBuffer = new Size(mapCanvas.Width + 1, mapCanvas.Height + 1);
        _canvasGraphics = mapCanvas.CreateGraphics();
        bufferedGraphics = bufferedGraphicsContext.Allocate(_canvasGraphics, mapCanvas.ClientRectangle);

        // All
        comboViewType.SelectedIndex = 6;
        checkEnableCollisions.Checked = Kernel.EnableCollisionDetection;
        tabNavMeshViewer.Visible = Kernel.Debug;

        if (Kernel.Debug)
        {
            _navMeshRenderer = new NavMeshRenderer() { Dock = DockStyle.Fill };

            panelNavMeshRendererCanvas.Controls.Add(_navMeshRenderer);
            labelSectorInfo.Visible = true;
        }
    }

    #region Core Handlers

    private void OnEnterGame()
    {
        ClearCachedImages();
        _currentSectorGraphic?.Dispose();
        _currentSectorGraphic = null;
        _currentRegion = null;
        _currentFloorName = null;
        _regionName = string.Empty;
        if (_mapEntityImages == null)
            _mapEntityImages = new[]
            {
                Game.MediaPk2.GetFile("interface\\minimap\\mm_sign_character.ddj").ToImage(),
                Game.MediaPk2.GetFile("interface\\minimap\\mm_sign_animal.ddj").ToImage(),
                Game.MediaPk2.GetFile("interface\\minimap\\mm_sign_npc.ddj").ToImage(),
                Game.MediaPk2.GetFile("interface\\minimap\\mm_sign_otherplayer.ddj").ToImage(),
                Game.MediaPk2.GetFile("interface\\minimap\\mm_sign_monster.ddj").ToImage(),
                Game.MediaPk2.GetFile("interface\\minimap\\mm_sign_unique.ddj").ToImage(),
                Game.MediaPk2.GetFile("interface\\minimap\\mm_sign_party.ddj").ToImage(),
                Game.MediaPk2.GetFile("interface\\minimap\\mm_sign_unique.ddj").ToImage(),
            };
    }

    #endregion Core Handlers

    /// <summary>
    ///     Adds the grid item. The row is collected and shown by <see cref="UpdateGrid" />.
    /// </summary>
    /// <param name="key">Identifies the row between updates.</param>
    /// <param name="name">The name.</param>
    /// <param name="type">The type.</param>
    /// <param name="level">The level.</param>
    /// <param name="position"></param>
    private void AddGridItem(string key, string name, string type, byte level, Position position)
    {
        _gridRows.Add((key, name, type, level, position));
    }

    /// <summary>
    ///     Updates the grid rows in place, so the selection and scroll position stay where the user left them.
    /// </summary>
    private void UpdateGrid()
    {
        var keys = new HashSet<string>();

        lvMonster.BeginUpdate();
        try
        {
            foreach (var row in _gridRows)
            {
                if (!keys.Add(row.Key))
                    continue;

                var name = string.IsNullOrWhiteSpace(row.Name) ? LanguageManager.GetLang("NoName") : row.Name;
                var level = row.Level.ToString();
                var position = row.Position.ToString();

                var item = lvMonster.Items[row.Key];
                if (item == null)
                {
                    item = new ListViewItem(name) { Name = row.Key };
                    item.SubItems.Add(row.Type);
                    item.SubItems.Add(level);
                    item.SubItems.Add(position);
                    lvMonster.Items.Add(item);

                    continue;
                }

                SetText(item.SubItems[0], name);
                SetText(item.SubItems[1], row.Type);
                SetText(item.SubItems[2], level);
                SetText(item.SubItems[3], position);
            }

            for (var i = lvMonster.Items.Count - 1; i >= 0; i--)
                if (!keys.Contains(lvMonster.Items[i].Name))
                    lvMonster.Items.RemoveAt(i);
        }
        finally
        {
            lvMonster.EndUpdate();
        }

        static void SetText(ListViewItem.ListViewSubItem subItem, string text)
        {
            if (subItem.Text != text)
                subItem.Text = text;
        }
    }

    /// <summary>
    ///     Draws the point at.
    /// </summary>
    private void DrawPointAt(Graphics gfx, Position position, int entityIndex)
    {
        var distanceToPosition = position.DistanceToPlayer();
        if (distanceToPosition > 150)
            return;

        try
        {
            var x = GetMapX(position);
            var y = GetMapY(position);

            var img = _mapEntityImages[entityIndex];

            if (entityIndex == 0)
                gfx.DrawImage(GetPlayerArrow(img), x - img.Width / 2, y - img.Height / 2);
            else
                gfx.DrawImage(img, x - img.Width / 2, y - img.Height / 2);
        }
        catch { }
    }

    /// <summary>
    ///     Gets the player arrow rotated to the player's direction, in 5 degree steps.
    /// </summary>
    private Bitmap GetPlayerArrow(Image arrow)
    {
        var angle = (int)Math.Round(Geometry.RadianToDegree(Game.Player.Movement.Angle) / 5f) * 5;
        if (_playerArrow == null || angle != _playerArrowAngle)
        {
            _playerArrow?.Dispose();
            _playerArrow = RotateImage(arrow, angle);
            _playerArrowAngle = angle;
        }

        return _playerArrow;
    }

    private void DrawRectangleAt(Graphics gfx, Position position, Brush brush, Size size, string label = "")
    {
        var distanceToPosition = position.DistanceToPlayer();
        if (distanceToPosition > 150)
            return;

        try
        {
            var x = GetMapX(position);
            var y = GetMapY(position);

            if (!string.IsNullOrEmpty(label))
                gfx.DrawString(label, Font, brush, x + size.Width, y - size.Width / 2);

            gfx.FillRectangle(brush, new RectangleF(new PointF(x, y), size));
        }
        catch { }
    }

    private void DrawLineAt(Graphics gfx, Position source, Position destination, Pen color)
    {
        var distanceToPositionA = source.DistanceToPlayer();
        var distanceToPositionB = destination.DistanceToPlayer();
        if (distanceToPositionA > 150 || distanceToPositionB > 150)
            return;

        var srcX = GetMapX(source);
        var srcY = GetMapY(source);
        var destinationX = GetMapX(destination);
        var destinationY = GetMapY(destination);

        gfx.DrawLine(color, srcX, srcY, destinationX, destinationY);
    }

    private void DrawCircleAt(Graphics gfx, Position position, Color color, int diameter)
    {
        var distanceToPosition = position.DistanceToPlayer();
        if (distanceToPosition > 150)
            return;

        try
        {
            var x = GetMapX(position);
            var y = GetMapY(position);

            var colorKey = color.ToArgb();
            if (!_circleBrushes.TryGetValue(colorKey, out var brush))
                _circleBrushes[colorKey] = brush = new SolidBrush(color);

            var diameterF = diameter * _scale;
            var point = new PointF(x - diameterF / 2, y - diameterF / 2);

            gfx.FillEllipse(brush, new RectangleF(point, new SizeF(diameterF, diameterF)));
            if (!_circlePens.TryGetValue(colorKey, out var pen))
                _circlePens[colorKey] = pen = new Pen(color);
            gfx.DrawEllipse(pen, new RectangleF(point, new SizeF(diameterF, diameterF)));
        }
        catch { }
    }

    /// <summary>
    ///     Fills the grid.
    /// </summary>
    private void PopulateMapAndGrid(Graphics graphics)
    {
        _gridRows.Clear();

        try
        {
            if (Game.Player.Movement.HasDestination)
            {
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                DrawLineAt(graphics, Game.Player.Movement.Source, Game.Player.Movement.Destination, _destinationPen);

                DrawCircleAt(graphics, Game.Player.Movement.Destination, Color.PaleGreen, 4);
                graphics.SmoothingMode = SmoothingMode.HighSpeed;
            }

            //Draw walk script
            if (ScriptManager.Running)
            {
                var walkScript = ScriptManager.GetWalkScript();
                for (var i = 0; i < walkScript.Count; i++)
                {
                    var nextPosition = walkScript[i];

                    DrawLineAt(graphics, i != 0 ? walkScript[i - 1] : nextPosition, nextPosition, Pens.LightBlue);
                    DrawCircleAt(graphics, nextPosition, Color.CornflowerBlue.Alpha(150), 4);
                }
            }

            if (Kernel.Bot.Running)
            {
                var position = Kernel.Bot.Botbase.Area.Position;
                var radius = Kernel.Bot.Botbase.Area.Radius;

                DrawCircleAt(graphics, position, Color.DarkRed.Alpha(100), radius * 2);
                DrawCircleAt(graphics, position, Color.LawnGreen.Alpha(50), radius);
            }

            if (comboViewType.SelectedIndex == 0 || comboViewType.SelectedIndex == 6)
                if (SpawnManager.TryGetEntities<SpawnedMonster>(out var monsters))
                    foreach (var entry in monsters)
                    {
                        AddGridItem(
                            $"m{entry.UniqueId}",
                            entry.Record.GetRealName(),
                            entry.Rarity.GetName(),
                            entry.Record.Level,
                            entry.Movement.Source
                        );

                        if (Game.SelectedEntity?.UniqueId == entry.UniqueId)
                            DrawCircleAt(graphics, entry.Position, Color.Wheat.Alpha(100), 6);

                        if (entry.Rarity == MonsterRarity.Unique || entry.Rarity == MonsterRarity.Unique2)
                            DrawPointAt(graphics, entry.Position, 5);
                        else
                            DrawPointAt(graphics, entry.Position, 4);
                    }

            if (comboViewType.SelectedIndex == 4 || comboViewType.SelectedIndex == 6)
                if (SpawnManager.TryGetEntities<SpawnedCos>(out var coses))
                    foreach (var entry in coses)
                        // Avoid painting vehicles from main player
                        if (Game.Player.Vehicle?.UniqueId != entry.UniqueId)
                        {
                            AddGridItem($"c{entry.UniqueId}", entry.Name, entry.Record?.GetRealName(), entry.Record.Level, entry.Movement.Source);
                            DrawPointAt(graphics, entry.Movement.Source, 1);
                        }

            if (comboViewType.SelectedIndex == 2 || comboViewType.SelectedIndex == 6)
                if (Game.Party != null && Game.Party.Members != null)
                    foreach (var member in Game.Party.Members.ToArray())
                    {
                        if (Game.Player.Position.DistanceTo(member.Position) > 50)
                            continue;

                        if (member.Name == Game.Player.Name)
                            continue;

                        DrawPointAt(graphics, member.Position, 6);
                        AddGridItem($"p{member.Name}", member.Name, "Party Member", member.Level, member.Position);
                    }

            if (comboViewType.SelectedIndex == 1 || comboViewType.SelectedIndex == 6)
                if (SpawnManager.TryGetEntities<SpawnedPlayer>(out var players))
                    foreach (var entry in players)
                    {
                        if (
                            Game.Party != null
                            && Game.Party.Members != null
                            && Game.Party.GetMemberByName(entry.Name) != null
                        )
                            continue;

                        AddGridItem($"u{entry.UniqueId}", entry.Name, "Player", 0, entry.Movement.Source);
                        DrawPointAt(graphics, entry.Movement.Source, 3);
                    }

            if (comboViewType.SelectedIndex == 3 || comboViewType.SelectedIndex == 6)
                if (SpawnManager.TryGetEntities<SpawnedNpcNpc>(out var npcs))
                    foreach (var entry in npcs)
                    {
                        AddGridItem(
                            $"n{entry.UniqueId}",
                            entry.Record.GetRealName(),
                            entry.UniqueId.ToString(),
                            entry.Record.Level,
                            entry.Movement.Source
                        );
                        DrawPointAt(graphics, entry.Movement.Source, 2);
                    }

            if (comboViewType.SelectedIndex == 5 || comboViewType.SelectedIndex == 6)
                if (SpawnManager.TryGetEntities<SpawnedPortal>(out var portals))
                    foreach (var entry in portals)
                    {
                        AddGridItem($"t{entry.UniqueId}", entry.Record.GetRealName(), "Teleport", 0, entry.Movement.Source);
                        DrawPointAt(graphics, entry.Movement.Source, 7);
                    }
        }
        catch (Exception ex)
        {
            Log.Debug($"[Map] Render error: {ex.Message}");
        }

        // The canvas is drawn every frame, the list only needs a slower refresh
        if (DateTime.Now - _lastGridUpdate >= TimeSpan.FromSeconds(1))
        {
            _lastGridUpdate = DateTime.Now;
            UpdateGrid();
        }
    }

    private Image LoadSectorImage(string sectorImgName)
    {
        if (_cachedImages.TryGetValue(sectorImgName, out var cachedImage))
            return (Image)cachedImage.Clone();

        Image image;
        try
        {
            if (!Game.MediaPk2.FileExists(sectorImgName))
                throw new System.IO.FileNotFoundException("Tile is missing from Media.pk2.");

            image = Game.MediaPk2.GetFile(sectorImgName).ToImage(throwOnError: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Map] Cannot load '{sectorImgName}': {ex.Message}");
            image = new Bitmap(SectorSize, SectorSize);
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.FromArgb(32, 32, 32));
            graphics.DrawRectangle(Pens.DimGray, 0, 0, SectorSize - 1, SectorSize - 1);
            graphics.DrawString("Map tile unavailable", Font, Brushes.LightGray, 8, 8);
        }

        // Cache failed tiles as well so the timer does not repeat warnings every frame.
        _cachedImages.Add(sectorImgName, image);
        return (Image)image.Clone();
    }

    private void ClearCachedImages()
    {
        foreach (var image in _cachedImages.Values)
            image.Dispose();

        _cachedImages.Clear();
    }

    /// <summary>
    ///     Redraw the map image
    /// </summary>
    private void RedrawMap()
    {
        var size = mapCanvas.ClientSize;

        if (
            bufferedGraphicsContext.MaximumBuffer.Width < size.Width
            || bufferedGraphicsContext.MaximumBuffer.Height < size.Height
        )
        {
            bufferedGraphicsContext.MaximumBuffer = new Size(size.Width + 1, size.Height + 1);
        }

        if (
            bufferedGraphics == null
            || bufferedGraphics.Graphics.VisibleClipBounds.Width != size.Width
            || bufferedGraphics.Graphics.VisibleClipBounds.Height != size.Height
        )
        {
            bufferedGraphics?.Dispose();
            _canvasGraphics?.Dispose();
            _canvasGraphics = mapCanvas.CreateGraphics();
            bufferedGraphics = bufferedGraphicsContext.Allocate(_canvasGraphics, mapCanvas.ClientRectangle);
        }

        // Set layer path & sectors
        var p = Game.Player.Movement.Source;

        var tempX = p.Region.X;
        var tempY = p.Region.Y;

        if (p.Region.IsDungeon)
        {
            tempX = p.GetSectorFromOffset(p.XOffset);
            tempY = p.GetSectorFromOffset(p.YOffset);
        }

        try
        {
            var floorName = string.Empty;
            var dungeonName = string.Empty;
            if (p.Region.IsDungeon)
            {
                if (!p.TryGetNavMeshTransform(out var pTransform))
                    return;

                if (pTransform.Instance is not NavMeshInstBlock dungeonBlock)
                    return;

                if (dungeonBlock.Parent is not NavMeshDungeon dungeon)
                    return;

                floorName = dungeon.FloorStringIDs[dungeonBlock.FloorIndex]; //e.g "DH_A01_FLOOR01"
                var roomName = dungeon.RoomStringIDs[dungeonBlock.RoomIndex];
                var roomNameTranslated = Game.ReferenceManager.GetTranslation(roomName);

                _regionName = roomNameTranslated ?? string.Empty;
                dungeonName = RegionInfoManager.GetDungeonName(p.Region);
            }
            else
                _regionName = Game.ReferenceManager.GetTranslation(Game.Player.Position.Region.ToString()) ?? string.Empty;

            if (
                _currentSectorGraphic != null
                && _currentRegion == p.Region.Id
                && tempX == _currentXSec
                && tempY == _currentYSec
                && floorName == _currentFloorName
            )
                return;

            if (_cachedImages.Count >= 25)
                ClearCachedImages();

            using var sectorGraphic = new Bitmap(SectorSize * GridSize, SectorSize * GridSize, PixelFormat.Format32bppPArgb);
            using var gfx = Graphics.FromImage(sectorGraphic);
            gfx.InterpolationMode = InterpolationMode.Bicubic;

            for (byte x = 0; x < GridSize; x++)
            {
                for (byte z = 0; z < GridSize; z++)
                {
                    var xSector = (byte)(tempX + x - 1);
                    var ySector = (byte)(tempY + z - 1);

                    var sectorImgName = GetMinimapFileName(new Region(xSector, ySector), dungeonName, floorName);
                    using var bitmap = LoadSectorImage(sectorImgName);
                    var pos = new Point(SectorSize * x, SectorSize * (GridSize - 1 - z));

                    gfx.DrawImage(bitmap, new Rectangle(pos, new Size(SectorSize, SectorSize)));

                    if (Kernel.Debug)
                    {
                        using var pen = new Pen(Color.Black);
                        pen.DashStyle = DashStyle.Dot;
                        gfx.DrawRectangle(pen, new Rectangle(pos, new Size(SectorSize, SectorSize)));
                    }
                }
            }

            var previousGraphic = _currentSectorGraphic;
            _currentSectorGraphic = (Image)sectorGraphic.Clone();
            previousGraphic?.Dispose();
            _currentXSec = tempX;
            _currentYSec = tempY;
            _currentRegion = p.Region.Id;
            _currentFloorName = floorName;
        }
        catch (Exception e)
        {
            Log.Warn($"Error in minimap: {e.Message}");
        }
    }

    private string GetMinimapFileName(Region region, string dungeonName, string floorName)
    {
        if (!string.IsNullOrWhiteSpace(dungeonName) && !string.IsNullOrWhiteSpace(floorName))
        {
            return $"minimap_d\\{dungeonName}\\{floorName}_{region.X}x{region.Y}.ddj";
        }

        return $"minimap\\{region.X}x{region.Y}.ddj";
    }

    private Bitmap RotateImage(Image image, float angle)
    {
        var sizedBitmap = new Bitmap(image.Width + 1, image.Height + 1);

        using (var matrix = new Matrix())
        {
            matrix.RotateAt(angle, new PointF(sizedBitmap.Width / 2, sizedBitmap.Height / 2));

            sizedBitmap.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(sizedBitmap))
            {
                graphics.Transform = matrix;
                graphics.InterpolationMode = InterpolationMode.Bicubic;
                graphics.DrawImage(image, 1, 1);
            }
        }

        sizedBitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);

        return sizedBitmap;
    }

    private void DrawObjects(Graphics graphics)
    {
        if (_currentSectorGraphic != null)
        {
            graphics.InterpolationMode = InterpolationMode.Bicubic;
            graphics.SmoothingMode = SmoothingMode.HighSpeed;
            graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;

            PointF point = new()
            {
                X = mapCanvas.Width / 2f - SectorSize - Game.Player.Movement.Source.XSectorOffset / 10f * _scale,
                Y = mapCanvas.Height / 2f - SectorSize * 2f + Game.Player.Movement.Source.YSectorOffset / 10f * _scale,
            };

            graphics.DrawImage(_currentSectorGraphic, point);

            PopulateMapAndGrid(graphics);
            DrawPointAt(graphics, Game.Player.Movement.Source, 0);
        }
    }

    private void trmInterval_Tick(object sender, EventArgs e)
    {
        if (Game.Player == null)
            return;

        if (!Visible || !Enabled || FindForm()?.WindowState == FormWindowState.Minimized)
            return;

        if (Kernel.Debug)
        {
            labelSectorInfo.Text =
                $"{Game.Player.Movement.Source.Region} ({Game.Player.Movement.Source.Region.X}x{Game.Player.Movement.Source.Region.Y})";

            if (Kernel.EnableCollisionDetection && Game.Player.Position.TryGetNavMeshTransform(out var playerTransform))
                _navMeshRenderer?.Update(playerTransform);
        }

        // Navigation updates above still run when its page is selected.
        if (!mapCanvas.Visible)
            return;

        RedrawMap();
        bufferedGraphics.Graphics.Clear(Color.Black);
        DrawObjects(bufferedGraphics.Graphics);

        var font = _boldFont ??= new Font(Font, FontStyle.Bold);

        var text = Game.Player.Position.ToString();
        var measuredText = bufferedGraphics.Graphics.MeasureString(text, font);

        var x = mapCanvas.Width - measuredText.Width;

        bufferedGraphics.Graphics.DrawString(_regionName, font, Brushes.Black, 12, 7);
        bufferedGraphics.Graphics.DrawString(_regionName, font, Brushes.White, 10, 5);

        bufferedGraphics.Graphics.DrawString(text, font, Brushes.Black, x - 8, 21 - measuredText.Height / 2);
        bufferedGraphics.Graphics.DrawString(text, font, Brushes.White, x - 10, 20 - measuredText.Height / 2);

        mapCanvas.Invalidate();
    }

    private float GetMapX(Position gamePosition)
    {
        return mapCanvas.Width / 2f + (gamePosition.X - Game.Player.Movement.Source.X) * _scale;
    }

    private float GetMapY(Position gamePosition)
    {
        return mapCanvas.Height / 2f + (gamePosition.Y - Game.Player.Movement.Source.Y) * _scale * -1.0f;
    }

    private async void mapCanvas_MouseClick(object sender, MouseEventArgs e)
    {
        if (!Game.Ready)
            return;

        var position = Game.Player.Movement.Source;
        position.XOffset =
            Game.Player.Movement.Source.XOffset + (mapCanvas.Width / 2f - e.X) / SectorSize * 192f * 10 * -1f;
        position.YOffset = Game.Player.Movement.Source.YOffset + (mapCanvas.Height / 2f - e.Y) / SectorSize * 192f * 10;

        if (!position.Region.IsDungeon)
        {
            // Outdoor offsets must stay within a sector; movement packets carry the target region.
            var xSectors = (int)Math.Floor(position.XOffset / 1920f);
            var ySectors = (int)Math.Floor(position.YOffset / 1920f);
            var targetX = position.Region.X + xSectors;
            var targetY = position.Region.Y + ySectors;
            if (targetX < 0 || targetX > byte.MaxValue || targetY < 0 || targetY >= 128)
                return;

            position.Region = new Region((byte)targetX, (byte)targetY);
            position.XOffset -= xSectors * 1920f;
            position.YOffset -= ySectors * 1920f;
        }

        var player = Game.Player;
        try
        {
            await Task.Run(() => player.MoveTo(position, false));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex);
        }
    }

    private void checkEnableCollisions_CheckedChanged(object sender, EventArgs e)
    {
        Kernel.EnableCollisionDetection = checkEnableCollisions.Checked;
    }

    /// <summary>
    ///     Occurs before Main form is displayed.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Main_Load(object sender, EventArgs e)
    {
        _autoSelectUnqiue = PlayerConfig.Get("RSBot.Map.AutoSelectUnique", false);
        checkBoxAutoSelectUniques.Checked = _autoSelectUnqiue;

        checkBoxAutoSelectUniques.Enabled = Game.Player != null;
        timerUniqueChecker.Enabled = _autoSelectUnqiue && Game.Player != null;
    }

    private void btnNvmResetToPlayer_Click(object sender, EventArgs e)
    {
        if (Game.Player.Position.TryGetNavMeshTransform(out var playerTransform))
            _navMeshRenderer?.Update(playerTransform);
    }

    private void tabMinimap_Paint(object sender, PaintEventArgs e)
    {
        bufferedGraphics.Render();
    }

    /// <summary>
    ///     Initialize all objects for uniques.
    /// </summary>
    public void InitUniqueObjects()
    {
        _autoSelectUnqiue = PlayerConfig.Get("RSBot.Map.AutoSelectUnique", false);

        checkBoxAutoSelectUniques.Enabled = true;
        timerUniqueChecker.Enabled = _autoSelectUnqiue;
    }

    /// <summary>
    /// Auto select uniuqe
    /// </summary>
    private void checkBoxAutoSelectUniques_CheckedChanged(object sender, EventArgs e)
    {
        PlayerConfig.Set("RSBot.Map.AutoSelectUnique", checkBoxAutoSelectUniques.Checked);
        _autoSelectUnqiue = checkBoxAutoSelectUniques.Checked;

        timerUniqueChecker.Enabled = _autoSelectUnqiue;
    }

    /// <summary>
    /// Ticker Event for auto select unique
    /// </summary>
    private async void timerUniqueChecker_Tick(object sender, EventArgs e)
    {
        if (!Visible || !Enabled || FindForm()?.WindowState == FormWindowState.Minimized)
            return;

        if (Game.Player == null)
            return;

        if (!_autoSelectUnqiue)
            return;

        if (Kernel.Bot.Running)
            return;

        // Check if Unique is on map.
        if (SpawnManager.TryGetEntities<SpawnedMonster>(out var monsters))
        {
            bool atleastOneUnqiueFound = false;

            foreach (var entry in monsters)
            {
                if ((entry.Rarity == MonsterRarity.Unique || entry.Rarity == MonsterRarity.Unique2))
                {
                    // Check if unique was already discovered.
                    // If yes => dont play sound again.
                    if (false == _isUniqueSpotted)
                    {
                        _isUniqueSpotted = true;

                        Game.Player.NotificationSounds?.PlayUniqueInRange();
                    }

                    atleastOneUnqiueFound = true;
                }
            }

            // If no unique was discovered, reset
            // _uniqueSpotted, so sound can play
            // again if unique is in sight again.
            if (!atleastOneUnqiueFound)
            {
                _isUniqueSpotted = false;
            }
        }
        else
        {
            _isUniqueSpotted = false;
        }

        if (
            Game.SelectedEntity?.Record.Rarity == ObjectRarity.ClassD
            || Game.SelectedEntity?.Record.Rarity == ObjectRarity.ClassI
        )
            return;

        if (
            SpawnManager.TryGetEntity<SpawnedMonster>(
                p => p.Record.Rarity == ObjectRarity.ClassD || p.Record.Rarity == ObjectRarity.ClassI,
                out var uniqueEntity
            )
        )
        {
            timerUniqueChecker.Stop();
            try
            {
                await Task.Run(() => uniqueEntity.TrySelect());
            }
            catch (Exception ex)
            {
                Log.Fatal(ex);
            }
            finally
            {
                if (!IsDisposed && !Disposing && IsHandleCreated && _autoSelectUnqiue)
                    timerUniqueChecker.Start();
            }
        }
    }
}
