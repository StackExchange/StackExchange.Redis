using System.Diagnostics;
using RESPite.Resp.KeyValueStore;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

public class CreateKeysDialog : ServerToolDialog
{
    public CreateKeysDialog()
    {
        Title = "Create keys";
        var countLabel = new Label
        {
            Text = "Count",
            X = 1,
            Y = 1,
        };
        var countField = new TextField()
        {
            Text = "250",
            X = Pos.Right(countLabel) + 1,
            Y = countLabel.Y,
            Width = Dim.Fill(),
        };

        var sizeLabel = new Label
        {
            Text = "Size",
            X = countLabel.X,
            Y = Pos.Bottom(countLabel),
        };
        var sizeField = new TextField()
        {
            Text = "1000",
            X = Pos.Right(sizeLabel) + 1,
            Y = sizeLabel.Y,
            Width = Dim.Fill(),
        };

        var btn = new Button
        {
            Text = "Create",
            IsDefault = true,
            X = countLabel.X,
            Y = Pos.Bottom(sizeLabel) + 2,
        };
        btn.Accept += async (s, e) =>
        {
            try
            {
                if (!int.TryParse(countField.Text, out int count))
                {
                    StatusText = "Unable to parse count";
                }
                else if (!int.TryParse(sizeField.Text, out int size))
                {
                    StatusText = "Unable to parse size";
                }
                else
                {
                    StatusText = $"Creating {count} records with {size} bytes each...";
                    byte[] key = new byte[36];
                    byte[] payload = new byte[size];
                    int i;

                    for (i = 0; i < count; i++)
                    {
                        bool didWrite = Guid.NewGuid().TryFormat(key, out int keyBytes) && keyBytes == key.Length;
                        Debug.Assert(didWrite);

                        var alphabet = Alphabet;
                        for (int j = 0; j < payload.Length; j++)
                        {
                            payload[j] = alphabet[Random.Shared.Next(0, alphabet.Length)];
                        }

                        await Strings.SETEX.SendAsync(Transport, (new(key), 5 * 60, new(payload)), CancellationToken);

                        if ((i % 20) == 0)
                        {
                            StatusText = $"{i} keys created";
                        }
                    }
                    StatusText = $"{i} keys created";
                }
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        };
        Add(countLabel, countField, sizeLabel, sizeField, btn);
    }

    private static ReadOnlySpan<byte> Alphabet => "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789 _+-/\\()!@#$%^&*[]{};:,.<>"u8;
}
