using System;

namespace Pixelpipe
{
    // What stats a given rclone backend can report. Each profile uses these flags
    // so the UI can show "Storage: 4.2 GB / 100 GB used" when the provider
    // actually reports those numbers and "Storage: not reported by this provider"
    // when it doesn't, instead of a misleading "0" or "unavailable".
    //
    // The flags are best-effort defaults; if a particular server (e.g. a WebDAV
    // box that does support quota) returns real numbers, we still show them. The
    // flags only control *labels* when rclone returns nothing.
    internal sealed class ProviderCapabilities
    {
        public readonly string Provider;
        public readonly bool SupportsStorageQuota;
        public readonly bool SupportsTransferQuota;
        public readonly bool SupportsFileCount;
        public readonly string TransferQuotaNotApplicableLabel;
        public readonly string StorageNotReportedLabel;

        private ProviderCapabilities(string provider, bool storage, bool transfer, bool fileCount, string transferLabel, string storageLabel)
        {
            Provider = provider;
            SupportsStorageQuota = storage;
            SupportsTransferQuota = transfer;
            SupportsFileCount = fileCount;
            TransferQuotaNotApplicableLabel = transferLabel;
            StorageNotReportedLabel = storageLabel;
        }

        public static ProviderCapabilities For(string providerOrRemote)
        {
            string p = TrayContext.NormalizeProvider(providerOrRemote, "");
            switch (p)
            {
                case "pixeldrain":
                    return new ProviderCapabilities(p, true, true, true,
                        "Transfer quota: PixelDrain API key not set",
                        "not reported by backend");
                case "drive":
                    return new ProviderCapabilities(p, true, false, true,
                        "Transfer quota: not applicable for Google Drive",
                        "not reported by backend");
                case "mega":
                    return new ProviderCapabilities(p, true, true, true,
                        "Transfer quota: see MEGA web account for current limits",
                        "not reported by backend");
                case "onedrive":
                    return new ProviderCapabilities(p, true, false, true,
                        "Transfer quota: not applicable for OneDrive",
                        "not reported by backend");
                case "dropbox":
                    return new ProviderCapabilities(p, true, false, true,
                        "Transfer quota: not applicable for Dropbox",
                        "not reported by backend");
                case "box":
                    return new ProviderCapabilities(p, true, false, true,
                        "Transfer quota: not applicable for Box",
                        "not reported by backend");
                case "s3":
                    return new ProviderCapabilities(p, false, false, false,
                        "Transfer quota: not applicable for S3-compatible buckets",
                        "not applicable for S3-compatible buckets");
                case "webdav":
                    return new ProviderCapabilities(p, false, false, false,
                        "Transfer quota: not applicable for WebDAV",
                        "depends on server; not reported");
                case "sftp":
                    return new ProviderCapabilities(p, true, false, false,
                        "Transfer quota: not applicable for SFTP",
                        "depends on server; not reported");
                case "ftp":
                    return new ProviderCapabilities(p, false, false, false,
                        "Transfer quota: not applicable for FTP",
                        "not reported by backend");
                default:
                    return new ProviderCapabilities("custom", true, false, true,
                        "Transfer quota: not tracked for custom remotes",
                        "not reported by backend");
            }
        }

        public string DefaultTransferQuotaText() { return TransferQuotaNotApplicableLabel; }
        public string DefaultStorageText()
        {
            if (!SupportsStorageQuota) return "not applicable for this provider";
            return "storage not checked";
        }
    }
}
