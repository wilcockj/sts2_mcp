import urllib.request

url = 'http://localhost:15526'
response = urllib.request.urlopen(url)
html_content = response.read().decode('utf-8')
print(html_content)
