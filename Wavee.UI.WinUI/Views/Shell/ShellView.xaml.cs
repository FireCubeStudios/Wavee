using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wavee.UI.Identity.Users.Contracts;
using Wavee.UI.ViewModels.Shell;
using Wavee.UI.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using CommunityToolkit.Mvvm.Messaging;
using Wavee.UI.AudioImport;
using Wavee.UI.AudioImport.Messages;

namespace Wavee.UI.WinUI.Views.Shell;

public sealed partial class ShellView : UserControl
{
    public ShellView(ShellViewModel viewmodel)
    {
        ViewModel = viewmodel;
        this.InitializeComponent();
    }
    public ShellViewModel ViewModel
    {
        get;
    }
    public NavigationService NavigationService { get; } = NavigationService.Instance;

    public bool IsLocal(ServiceType serviceType)
    {
        return serviceType == ServiceType.Local;
    }

    private void ShellView_OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Link;
        e.DragUIOverride.Caption = "Drop tracks"; // Sets custom UI text
        e.DragUIOverride.IsCaptionVisible = true; // Sets if the caption is visible
        e.DragUIOverride.IsContentVisible = true; // Sets if the dragged content is visible
        e.DragUIOverride.IsGlyphVisible = true; // Sets if the glyph is visibile
    }

    private async void ShellView_OnDrop(object sender, DragEventArgs e)
    {
        //check if we are on a playlist page

        //TODO: playlist view check
        if (NavigationService.Current is not { })
        {
            //should return false
        }
        else
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count == 0) return;

            //only accept audio files and folders 

            static bool IsAudioFile(IStorageItem item)
            {
                var isFile = item.IsOfType(StorageItemTypes.File);
                var contains = LocalAudioManagerViewModel.AcceptedAudioFormats.Contains(Path.GetExtension(item.Path));
                return isFile && contains;
            }

            static bool IsFolder(IStorageItem item)
            {
                return item.IsOfType(StorageItemTypes.Folder);
            }

            var audioFiles = items.Where(a => IsAudioFile(a) || IsFolder(a))
                .Select(a => (a.Path, IsFolder(a)));

            var message = new ImportTracksMessage(audioFiles);
            WeakReferenceMessenger.Default.Send(message);
        }
    }

    public Vector3 GetVectorFromHeight(double d)
    {
        return new Vector3(0, (float)-d, 0);
    }

}