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
    Safe print function that handles encoding errors.

    This prevents UnicodeEncodeError on Windows consoles (cp949, etc.)
    when printing characters that can't be encoded in the console's encoding.

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
        builtins.print(*args, **kwargs)
    except UnicodeEncodeError:
        # Fallback: encode with error handling
        message = ' '.join(str(arg) for arg in args)
        # Get console encoding, default to utf-8
        encoding = sys.stdout.encoding or 'utf-8'
        # Replace unencodable characters with '?'
        encoded = message.encode(encoding, errors='replace')
        decoded = encoded.decode(encoding)
        builtins.print(decoded, **kwargs)


__all__ = ['safe_print']
