"""
Safe print utility for handling encoding errors on Windows consoles.

This module provides a safe_print function that prevents UnicodeEncodeError
when printing Unicode characters that can't be encoded in the console's encoding
(e.g., cp949 on Korean Windows systems).

Usage:
    from utils.safe_print import safe_print

    safe_print("Hello — world")  # Won't crash even with em-dash
"""

import sys
import builtins


def safe_print(*args, **kwargs):
    """
    Safe print function that handles encoding errors and surrogate characters.

    This prevents UnicodeEncodeError on Windows consoles (cp949, etc.)
    when printing characters that can't be encoded in the console's encoding.
    Also handles surrogate characters that may come from improperly decoded data.

    Args:
        *args: Same as built-in print()
        **kwargs: Same as built-in print()

    Examples:
        >>> safe_print("Hello — world")  # em-dash won't cause issues
        Hello ? world  # if console can't handle em-dash

        >>> safe_print("Regular ASCII text")
        Regular ASCII text
    """
    try:
        # First, convert args to strings and handle surrogates
        cleaned_args = []
        for arg in args:
            arg_str = str(arg)
            # Check if the string contains surrogate characters
            try:
                # Try to encode normally first
                arg_str.encode('utf-8')
                cleaned_args.append(arg_str)
            except UnicodeEncodeError:
                # String contains surrogates - fix them
                try:
                    # Method 1: Convert surrogates back to original bytes and re-decode
                    original_bytes = arg_str.encode('utf-8', errors='surrogateescape')
                    cleaned = original_bytes.decode('utf-8', errors='replace')
                    cleaned_args.append(cleaned)
                except (UnicodeDecodeError, UnicodeEncodeError):
                    # Method 2: Just replace problematic characters
                    cleaned = arg_str.encode('utf-8', errors='replace').decode('utf-8')
                    cleaned_args.append(cleaned)

        builtins.print(*cleaned_args, **kwargs)
    except UnicodeEncodeError:
        # Fallback: encode with error handling
        message = ' '.join(str(arg) for arg in args)
        # Get console encoding, default to utf-8
        encoding = sys.stdout.encoding or 'utf-8'
        # Replace unencodable characters with '?'
        try:
            # Try to handle surrogates first
            original_bytes = message.encode('utf-8', errors='surrogateescape')
            cleaned_message = original_bytes.decode('utf-8', errors='replace')
            encoded = cleaned_message.encode(encoding, errors='replace')
        except (UnicodeDecodeError, UnicodeEncodeError):
            encoded = message.encode(encoding, errors='replace')
        decoded = encoded.decode(encoding)
        builtins.print(decoded, **kwargs)


__all__ = ['safe_print']
