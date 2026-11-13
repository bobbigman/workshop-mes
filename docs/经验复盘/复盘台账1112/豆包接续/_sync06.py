"""已停用旧版字符串替换同步，避免将旧数字写回。"""
import runpy
from pathlib import Path
runpy.run_path(str(Path(__file__).with_name("修订复核.py")))
