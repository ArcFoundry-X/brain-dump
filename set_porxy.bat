@echo off

setx HTTP_PROXY "http://127.0.0.1:7890"
setx HTTPS_PROXY "http://127.0.0.1:7890"

setx http_proxy "http://127.0.0.1:7890"
setx https_proxy "http://127.0.0.1:7890"

echo Proxy variables set permanently.
pause