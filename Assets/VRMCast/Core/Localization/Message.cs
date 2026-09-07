using System;

namespace VRMCast.Core.Localization
{
    /// <summary>
    /// A user-facing message expressed as a string key plus format arguments, so services stay language-neutral
    /// and the UI translates at display time. Arguments that are themselves <see cref="Message"/>s are translated
    /// recursively.
    /// </summary>
    public readonly struct Message : IEquatable<Message>
    {
        public string Key { get; }
        public object[] Args { get; }

        public Message(string key, params object[] args)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Args = args ?? Array.Empty<object>();
        }

        public static Message Of(string key, params object[] args) => new Message(key, args);

        public bool IsEmpty => string.IsNullOrEmpty(Key);

        public bool Equals(Message other) => string.Equals(Key, other.Key, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is Message m && Equals(m);
        public override int GetHashCode() => Key != null ? Key.GetHashCode() : 0;
        public override string ToString() => Key ?? string.Empty;
    }
}
