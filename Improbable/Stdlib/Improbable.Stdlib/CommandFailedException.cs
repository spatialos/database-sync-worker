using System;
using Improbable.Worker.CInterop;

namespace Improbable.Stdlib
{
    public class CommandFailedException : Exception
    {
        public CommandFailedException(StatusCode code, string message) :
            base(message)
        {
            Code = code;
        }

        public StatusCode Code { get; }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Message))
            {
                return $"{Code}";
            }

            return $"{Code}: {Message}";
        }
    }
}
