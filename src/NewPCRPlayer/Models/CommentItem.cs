using System.ComponentModel;
using System.Runtime.CompilerServices;
using NewPCRPlayer.Services.Bbs;

namespace NewPCRPlayer.Models;

/// <summary>UI row for one BBS post (supports new-post highlight + formatted header).</summary>
public sealed class CommentItem : INotifyPropertyChanged
{
    private bool _isNew;
    private string _displayHeader = "";

    public CommentItem(BbsPost post, AppSettings settings, bool isNew = false)
    {
        Post = post ?? throw new ArgumentNullException(nameof(post));
        _isNew = isNew;
        RefreshHeader(settings);
    }

    public BbsPost Post { get; }

    public int Number => Post.Number;
    public string BodyText => Post.BodyText;

    public string DisplayHeader
    {
        get => _displayHeader;
        private set
        {
            if (_displayHeader == value) return;
            _displayHeader = value;
            OnPropertyChanged();
        }
    }

    public bool IsNew
    {
        get => _isNew;
        set
        {
            if (_isNew == value) return;
            _isNew = value;
            OnPropertyChanged();
        }
    }

    public void RefreshHeader(AppSettings settings)
    {
        DisplayHeader = settings.FormatResHeader(Post);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
