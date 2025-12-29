#!/bin/bash
# Kairo Launcher 编译脚本
# 用法: ./build-launcher.sh [linux-x64|win-x64|osx-x64|...]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
LAUNCHER_DIR="$SCRIPT_DIR"
RESOURCES_DIR="$LAUNCHER_DIR/Resources"

# 默认 RID
RID="${1:-linux-x64}"

echo "=========================================="
echo "  Kairo Launcher Build Script"
echo "  Target: $RID"
echo "=========================================="

# 创建资源目录
mkdir -p "$RESOURCES_DIR"

# 步骤 1: 编译 Kairo (GUI)
echo ""
echo "[1/4] 编译 Kairo (GUI)..."
dotnet publish "$ROOT_DIR/Kairo/Kairo.csproj" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -o "$ROOT_DIR/publish/gui-$RID"

# 步骤 2: 编译 Kairo.Cli
echo ""
echo "[2/4] 编译 Kairo.Cli..."
dotnet publish "$ROOT_DIR/Kairo.Cli/Kairo.Cli.csproj" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -o "$ROOT_DIR/publish/cli-$RID"

# 步骤 3: 打包产物到 tar.gz 并生成哈希文件
echo ""
echo "[3/4] 打包编译产物并生成哈希..."

# 清理旧资源
rm -f "$RESOURCES_DIR/kairo-gui.tar.gz" "$RESOURCES_DIR/kairo-cli.tar.gz"
rm -f "$RESOURCES_DIR/kairo-gui.hashes" "$RESOURCES_DIR/kairo-cli.hashes"

# 函数：生成目录中所有文件的 MD5 哈希
generate_hashes() {
    local dir="$1"
    local output="$2"
    cd "$dir"
    # 使用 find 找到所有文件，排除 pdb 和 dbg，然后计算 md5
    find . -type f ! -name '*.pdb' ! -name '*.dbg' | while read -r file; do
        if command -v md5sum &> /dev/null; then
            md5sum "$file"
        elif command -v md5 &> /dev/null; then
            # macOS 使用 md5 命令
            md5 -r "$file" | awk '{print $1 "  " $2}'
        fi
    done > "$output"
}

# 打包 GUI（排除 pdb 和 dbg 文件）
echo "  打包 GUI..."
cd "$ROOT_DIR/publish/gui-$RID"
tar --exclude='*.pdb' --exclude='*.dbg' -czf "$RESOURCES_DIR/kairo-gui.tar.gz" .
generate_hashes "$ROOT_DIR/publish/gui-$RID" "$RESOURCES_DIR/kairo-gui.hashes"
echo "  - kairo-gui.tar.gz: $(du -h "$RESOURCES_DIR/kairo-gui.tar.gz" | cut -f1)"
echo "  - kairo-gui.hashes: $(wc -l < "$RESOURCES_DIR/kairo-gui.hashes") files"

# 打包 CLI（排除 pdb 和 dbg 文件）
echo "  打包 CLI..."
cd "$ROOT_DIR/publish/cli-$RID"
tar --exclude='*.pdb' --exclude='*.dbg' -czf "$RESOURCES_DIR/kairo-cli.tar.gz" .
generate_hashes "$ROOT_DIR/publish/cli-$RID" "$RESOURCES_DIR/kairo-cli.hashes"
echo "  - kairo-cli.tar.gz: $(du -h "$RESOURCES_DIR/kairo-cli.tar.gz" | cut -f1)"
echo "  - kairo-cli.hashes: $(wc -l < "$RESOURCES_DIR/kairo-cli.hashes") files"

cd "$SCRIPT_DIR"

# 步骤 4: 编译 Launcher
echo ""
echo "[4/4] 编译 Kairo.Launcher..."
dotnet publish "$LAUNCHER_DIR/Kairo.Launcher.csproj" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -o "$ROOT_DIR/publish/launcher-$RID"

# 最终产物
echo ""
echo "=========================================="
echo "  编译完成!"
echo "=========================================="
echo ""
echo "产物位置:"
echo "  - GUI:      $ROOT_DIR/publish/gui-$RID/"
echo "  - CLI:      $ROOT_DIR/publish/cli-$RID/"
echo "  - Launcher: $ROOT_DIR/publish/launcher-$RID/"
echo ""

# 显示 Launcher 大小
if [[ "$RID" == win* ]]; then
    LAUNCHER_EXE="kairo.exe"
else
    LAUNCHER_EXE="kairo"
fi

echo "Launcher 大小: $(du -h "$ROOT_DIR/publish/launcher-$RID/$LAUNCHER_EXE" | cut -f1)"
echo ""
echo "注意: Launcher 包含嵌入的 GUI 和 CLI 完整运行时"
echo "      总大小 ≈ GUI + CLI (已压缩)"
