rm -rf ./buildinfo.info

if [ -n "$GITHUB_ACTIONS" ]; then
  BUILD_TYPE="Github Action"
else
  BUILD_TYPE="local"
fi

COMMIT_HASH=$(git rev-parse --short HEAD 2>/dev/null || echo "unknown")

echo "$BUILD_TYPE" > ./buildinfo.info
echo "$(date +%Y-%m-%d) $(date +%H:%M:%S)" >> ./buildinfo.info
echo "$COMMIT_HASH" >> ./buildinfo.info
exit 0