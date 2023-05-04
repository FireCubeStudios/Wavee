namespace Wavee.Spotify.Playback.Cdn;

public class CdnException : Exception
{
    public CdnException(CdnUrlError err)
        : base(err switch
        {
            CdnUrlError.Expired => "all URLs expired",
            CdnUrlError.Storage => "resolved storage is not for CDN",
            CdnUrlError.Unresolved => "no URLs resolved",
            _ => throw new ArgumentOutOfRangeException(nameof(err), err, null)
        })
    {
        Err = err;
    }

    public CdnUrlError Err { get; }
}