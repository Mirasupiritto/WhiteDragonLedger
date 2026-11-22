"""Windows locale mapping used by stock and purchase-lot searches."""
import ctypes
import os


def chinese_form(text, traditional=False):
    if not text or os.name != "nt":
        return text
    convert = ctypes.windll.kernel32.LCMapStringEx
    flags = 0x04000000 if traditional else 0x02000000
    length = len(text.encode("utf-16-le")) // 2
    size = convert("zh-CN", flags, text, length, None, 0, None, None, 0)
    if not size:
        raise ctypes.WinError()
    output = ctypes.create_unicode_buffer(size + 1)
    if not convert("zh-CN", flags, text, length, output, size, None, None, 0):
        raise ctypes.WinError()
    return output.value


def search_text(text):
    return chinese_form(text.casefold())
