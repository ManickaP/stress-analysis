# Stress Analysis

This project is a WIP of automation to parse and analyze failures from our stress pipelines:
- https://dev.azure.com/dnceng-public/public/_build?definitionId=131 - HTTP
- https://dev.azure.com/dnceng-public/public/_build?definitionId=132 - SSL

## Current State

The project uses AzDO REST APIs to fetch the data for the past week for the HTTP stress. Then it filters the failing tasks, downloads the raw log files and stores them locally on the disk.
Then it tries to parse the error(s) from the individual log files and match them with the existing issues that were in the past linked with the stress tests, i.e. all issues mentioned in the [stress report issue](https://github.com/dotnet/runtime/issues/42211).
Note that the these issue numbers were manually picked from the issues and the program will use GH REST API to fetch info about them and store it locally on the disk in _downloaded-issues_ directory.

## AI Usage

The project uses AI to firstly parse the errors from the logs (`ExtractErrorPrompt` prompt), giving error message and occurrences.
Each error is then compared the pre-downloaded issue descriptions with AI (`MatchIssuePrompt` prompt), giving issue number and its confidence in the match.

### AI Learnings

First I started with vibe-coding the AzDO REST API, it got me started and then it was easy to continue on my own. Then I used copilot chat to try to play with prompts to get the wanted results for error parsing and issue matching. I was using o4-mini model, so that's what I selected in the AI Foundry on Azure. However, this model used programmatically was giving me very inconsistent results. And it was not possible to set Temperature and / or TopP. So I switched to gpt-o4 which allowed me to set these parameters and the results are now stable.

### AI Verdict

Hard to evaluate the results now, as we're not getting many errors at the moment and we have only one active stress related issue that might pop up. But with this limited data, it's so far stable and accurate.
Also, I enjoyed vibe-coding to get me started on APIs I don't know. It's much less daunting than starting with reading multi-page docs. After getting my toes wet, it's then much easier to go and pick specifics in the docs when I already have something partially working down.