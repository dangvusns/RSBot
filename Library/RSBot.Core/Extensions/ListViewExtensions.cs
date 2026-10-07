using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Skill;

namespace RSBot.Core.Extensions;

public static class ListViewExtensions
{
    /// <summary>
    ///     The cached image list for skills
    /// </summary>
    public static ImageList StaticImageList;

    /// <summary>
    ///     The cached image list for items
    /// </summary>
    public static ImageList StaticItemsImageList;

    /// <summary>
    ///     <inheritdoc />
    /// </summary>
    static ListViewExtensions()
    {
        StaticImageList = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(32, 32) };

        StaticItemsImageList = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(32, 32) };

        // With the native list created, added images are copied right away, so the decoded icon can be disposed
        try
        {
            _ = StaticImageList.Handle;
            _ = StaticItemsImageList.Handle;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"Image lists could not be created early: {e.Message}");
        }
    }

    /// <summary>
    ///     The keys already added to the image lists. <see cref="ImageList.ImageCollection.ContainsKey" /> is a linear search.
    /// </summary>
    private static readonly HashSet<string> _imageKeys = new();
    private static readonly HashSet<string> _itemImageKeys = new();

    /// <summary>
    ///     Adds the icon to the image list once. Call inside <see cref="_lock" />.
    /// </summary>
    private static void AddOnce(ImageList imageList, HashSet<string> keys, string key, Func<Image> createIcon)
    {
        if (keys.Contains(key))
            return;

        var icon = createIcon();
        imageList.Images.Add(key, icon);
        keys.Add(key);

        // Before the native list exists the image list keeps the original image
        if (imageList.HandleCreated)
            icon.Dispose();
    }

    /// <summary>
    ///     The sync object
    /// </summary>
    private static object _lock { get; } = new();

    /// <summary>
    ///     Load the skill image into the ImageList of the <seealso cref="ListViewItem" />
    /// </summary>
    public static Task LoadSkillImage(this ListViewItem listViewItem)
    {
        lock (_lock)
        {
            try
            {
                if (listViewItem.Tag is SkillInfo skill)
                {
                    var imageKey = "skill:" + skill.Id;
                    AddOnce(StaticImageList, _imageKeys, imageKey, () => skill.Record.GetIcon());

                    //Renders the image
                    listViewItem.ImageKey = imageKey;
                }

                if (listViewItem.Tag is ItemPerk perk)
                {
                    var imageKey = "perk:" + perk.ItemId;
                    AddOnce(StaticImageList, _imageKeys, imageKey, () => perk.Item?.GetIcon() ?? new Bitmap(1, 1));

                    listViewItem.ImageKey = imageKey;
                }
            }
            catch
            {
                Log.Debug("Error while loading skill image");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Load the item image into the ListViewItem
    /// </summary>
    public static Task LoadItemImage(this ListViewItem listViewItem, RefObjItem item)
    {
        lock (_lock)
        {
            //No need to reload the image from the PK2
            AddOnce(StaticItemsImageList, _itemImageKeys, item.CodeName, item.GetIcon);

            //Renders the image
            listViewItem.ImageKey = item.CodeName;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Load the skill image into the ImageList of the <seealso cref="ListViewItem" /> with async
    /// </summary>
    public static async void LoadSkillImageAsync(this ListViewItem item)
    {
        await item.LoadSkillImage();
    }

    /// <summary>
    ///     Load the skill image into the ImageList of the <seealso cref="ListViewItem" /> with async
    /// </summary>
    public static async void LoadItemImageAsync(this ListViewItem listViewItem, RefObjItem item)
    {
        await listViewItem.LoadItemImage(item);
    }

    /// <summary>
    ///     Load the skill image into the ImageList of the <seealso cref="ListViewItem" /> with async
    /// </summary>
    public static async void LoadItemImageAsync(this ListViewItem listViewItem, RefShopGood good)
    {
        try
        {
            var refPackageItem = Game.ReferenceManager.GetRefPackageItem(good.RefPackageItemCodeName);
            var refItem = Game.ReferenceManager.GetRefItem(refPackageItem.RefItemCodeName);

            await listViewItem.LoadItemImage(refItem);
        }
        catch { }
    }

    /// <summary>
    ///     Loads the skill images into the ImageList of the <seealso cref="ListView" /> control.
    /// </summary>
    public static async void LoadSkillImagesAsync(this ListView listView)
    {
        listView.BeginUpdate();

        foreach (ListViewItem item in listView.Items)
            await item.LoadSkillImage();

        listView.EndUpdate();
    }
}
