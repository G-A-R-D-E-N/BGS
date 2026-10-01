using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviourStudio.App;

internal sealed class CodexLineReader
{
    private readonly TextReader _reader;
    private readonly char[] _buffer;
    private int _start;
    private int _length;

    public CodexLineReader(TextReader reader, int bufferSize = 4096)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _buffer = new char[bufferSize > 0 ? bufferSize : 4096];
    }

    public int OversizedLineCount { get; private set; }

    public async Task<string?> ReadLineAsync(int maximumLength, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        bool oversized = false;

        while (true)
        {
            if (_start >= _length)
            {
                _length = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _start = 0;
                if (_length == 0)
                {
                    if (oversized) return "";
                    return builder.Length == 0 ? null : builder.ToString().TrimEnd('\r');
                }
            }

            int newline = Array.IndexOf(_buffer, '\n', _start, _length - _start);
            int end = newline >= 0 ? newline : _length;
            if (!oversized)
            {
                builder.Append(_buffer, _start, end - _start);
                if (builder.Length > maximumLength)
                {
                    oversized = true;
                    OversizedLineCount++;
                }
            }

            if (newline >= 0)
            {
                _start = newline + 1;
                return oversized ? "" : builder.ToString().TrimEnd('\r');
            }

            _start = _length;
        }
    }
}
