rm -rf ./buildinfo.info
echo "local" > ./buildinfo.info
echo "$(date +%Y-%m-%d) $(date +%H:%M:%S)" >> ./buildinfo.info
echo "null" >> ./buildinfo.info
exit 0