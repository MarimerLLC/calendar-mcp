using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CalendarMcp.Tests.Services;

[TestClass]
public class InMemoryAttachmentStoreTests
{
    private static InMemoryAttachmentStore CreateStore(
        AttachmentStoreOptions? options = null,
        TimeProvider? time = null)
    {
        return new InMemoryAttachmentStore(
            Options.Create(options ?? new AttachmentStoreOptions()),
            NullLogger<InMemoryAttachmentStore>.Instance,
            time);
    }

    [TestMethod]
    public void Put_Then_TryConsume_RoundTripsBytes()
    {
        var store = CreateStore();
        var data = "hello"u8.ToArray();

        var stored = store.Put("hello.txt", "text/plain", data);

        Assert.IsNotNull(stored);
        Assert.IsFalse(string.IsNullOrEmpty(stored!.Id));
        Assert.AreEqual("hello.txt", stored.Name);
        Assert.AreEqual("text/plain", stored.ContentType);
        CollectionAssert.AreEqual(data, stored.Bytes);

        var consumed = store.TryConsume(stored.Id);
        Assert.IsNotNull(consumed);
        CollectionAssert.AreEqual(data, consumed!.Bytes);

        // Single-use: a second consume returns null.
        Assert.IsNull(store.TryConsume(stored.Id));
    }

    [TestMethod]
    public void Put_RejectsOversizedFile()
    {
        var store = CreateStore(new AttachmentStoreOptions { MaxBytesPerAttachment = 100 });
        var stored = store.Put("big.bin", null, new byte[101]);
        Assert.IsNull(stored);
    }

    [TestMethod]
    public void Put_RejectsWhenGlobalCapWouldBeExceeded()
    {
        var store = CreateStore(new AttachmentStoreOptions
        {
            MaxBytesPerAttachment = 1000,
            MaxTotalBytes = 100,
        });

        Assert.IsNotNull(store.Put("a", null, new byte[60]));
        // Next 60 bytes would put us at 120, over the 100-byte global cap.
        Assert.IsNull(store.Put("b", null, new byte[60]));
    }

    [TestMethod]
    public void TryConsume_ExpiredEntry_ReturnsNull()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = CreateStore(
            new AttachmentStoreOptions { Ttl = TimeSpan.FromMinutes(1) },
            time);

        var stored = store.Put("x", null, new byte[10]);
        Assert.IsNotNull(stored);

        time.Advance(TimeSpan.FromMinutes(2));

        var consumed = store.TryConsume(stored!.Id);
        Assert.IsNull(consumed);
    }

    [TestMethod]
    public void EvictExpired_RemovesPastEntriesAndFreesQuota()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = CreateStore(
            new AttachmentStoreOptions
            {
                Ttl = TimeSpan.FromMinutes(1),
                MaxTotalBytes = 100,
                MaxBytesPerAttachment = 100,
            },
            time);

        var first = store.Put("a", null, new byte[80]);
        Assert.IsNotNull(first);

        time.Advance(TimeSpan.FromMinutes(2));
        store.EvictExpired();

        // Quota should be reclaimed: a fresh 80-byte upload now fits.
        var second = store.Put("b", null, new byte[80]);
        Assert.IsNotNull(second);
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
