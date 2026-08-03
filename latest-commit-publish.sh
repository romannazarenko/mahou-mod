#!/bin/bash
# First run the Mahou/build-all.cmd
if [ ! -d "BUILD" ]; then echo "No assets..."; exit 0; fi
API='https://gitea.com/api/v1/repos/BladeMight/Mahou'
TAG="latest-commit"
TOKEN="$(<~/.gitea)"
CSH=$(git log -1 --pretty=format:%h)
BID=$(git rev-list --count master)
body=$(git log -1 | tail -n +5 | jq -R -s '.')
name="latest commit release ([$CSH] build #$BID)"
data="{ 
  \"body\": $body,
  \"draft\":false,
  \"name\": \"$name\",
  \"prerelease\": true,
  \"tag_name\": \"$TAG\",
  \"target_commitish\": \"master\"
}"
#echo $data | jq
curl -I -L -s -X "DELETE" "$API/releases/tags/$TAG" -H 'accept: application/json' -H "Authorization: token $TOKEN"
curl -I -L -s -X "DELETE" "$API/tags/$TAG" -H 'accept: application/json' -H "Authorization: token $TOKEN"
sleep 2
id=$(curl -L -s -X "POST" "$API/releases" -H "Authorization: token $TOKEN" -H "Content-Type: application/json" -d "$data" | jq -r '.id')
echo "$id"
assets="Release_x86_x64.zip AS_Dict.zip jkl.zip Debug_x86.zip Debug_x64.zip Debug_x86_x64.zip Release_x86.zip Release_x64.zip"
cd BUILD
while IFS= read -r ANAME; do
  echo "Uploading $ANAME..."
  curl -i -L -X 'POST' \
    "$API/releases/$id/assets?name=$ANAME" \
    -H 'accept: application/json' \
    -H 'Content-Type: multipart/form-data' \
    -H "Authorization: token $TOKEN" \
    -F "attachment=@$ANAME;" \
    < /dev/null
done < <(echo "$assets" | tr ' ' '\n')