#requires -Version 7.0
function Test-LabelAuditPayload {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Json, [Parameter(Mandatory)][object[]]$SelectedRows)

    # Reviewed bounds: rows <= 32, properties <= 12, corpus <= 32,
    # candidates/alternatives <= 20, quotes <= 4; every loop checks deadline/cancel.
    $auditWatch = [Diagnostics.Stopwatch]::StartNew()
    $auditCancelPath = Join-Path $PSScriptRoot 'cancel.request'
    $auditDocument = $null
    function Assert-AuditBudget {
        if ($auditWatch.Elapsed.TotalSeconds -ge 30) { throw 'Label audit validation exceeded 30 seconds.' }
        if (Test-Path -LiteralPath $auditCancelPath) { throw 'Label audit validation cancelled.' }
    }
    function Require-AuditString([System.Text.Json.JsonElement]$Value, [string]$Name, [bool]$Nonempty) {
        Assert-AuditBudget
        if ($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) { throw "$Name must be a JSON string." }
        $auditString = $Value.GetString()
        if ($Nonempty -and [string]::IsNullOrWhiteSpace($auditString)) { throw "$Name must be nonempty." }
        return $auditString
    }
    function Require-AuditEnum([System.Text.Json.JsonElement]$Value, [string]$Name, [string[]]$Allowed) {
        $auditEnum = Require-AuditString $Value $Name $true
        if (-not ($Allowed -ccontains $auditEnum)) { throw "$Name has an unsupported value." }
        return $auditEnum
    }
    function Add-AuditCorpus([Collections.Generic.List[string]]$Corpus, [object]$Text) {
        Assert-AuditBudget
        if ($null -eq $Text) { return }
        if ($Text -isnot [string]) { throw 'Original text corpus contains a non-string field.' }
        if ($Corpus.Count -ge 32) { throw 'Original text corpus exceeds 32 segments.' }
        $Corpus.Add($Text)
    }
    try {
        Assert-AuditBudget
        if ($SelectedRows.Count -lt 1 -or $SelectedRows.Count -gt 32) { throw 'SelectedRows must contain 1..32 rows.' }
        if ([Text.Encoding]::UTF8.GetByteCount($Json) -gt 1048576) { throw 'Label audit JSON exceeds 1 MiB.' }
        $auditOptions = [System.Text.Json.JsonDocumentOptions]::new()
        $auditOptions.MaxDepth = 32
        $auditOptions.AllowTrailingCommas = $false
        $auditOptions.CommentHandling = [System.Text.Json.JsonCommentHandling]::Disallow
        $auditDocument = [System.Text.Json.JsonDocument]::Parse($Json, $auditOptions)
        $auditRoot = $auditDocument.RootElement
        if ($auditRoot.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw 'Top-level JSON must be an object.' }
        $auditTopNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $auditTopCount = 0
        foreach ($auditProperty in $auditRoot.EnumerateObject()) {
            Assert-AuditBudget
            $auditTopCount++
            if ($auditTopCount -gt 1 -or -not $auditTopNames.Add($auditProperty.Name) -or $auditProperty.Name -cne 'reviews') {
                throw 'Top-level JSON requires only one reviews property; duplicate/unknown properties are rejected.'
            }
        }
        if ($auditTopCount -ne 1) { throw 'Top-level reviews is required.' }
        $auditReviews = $auditRoot.GetProperty('reviews')
        if ($auditReviews.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or $auditReviews.GetArrayLength() -ne $SelectedRows.Count) {
            throw 'reviews must be an array covering the full selected denominator.'
        }
        $auditSources = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        for ($auditRowIndex = 0; $auditRowIndex -lt $SelectedRows.Count -and $auditRowIndex -lt 32; $auditRowIndex++) {
            Assert-AuditBudget
            $auditSource = $SelectedRows[$auditRowIndex]
            $auditQuestion = @($auditSource.request.questions)
            if ($auditQuestion.Count -ne 1 -or $auditSource.record_id -isnot [string] -or [string]::IsNullOrWhiteSpace($auditSource.record_id)) {
                throw 'Each selected source row requires one question and a record_id.'
            }
            if (-not $auditSources.TryAdd($auditSource.record_id, $auditSource)) { throw 'Selected source record_id is duplicated.' }
        }
        $auditRequired = @('record_id','question_id','clarity_status','answer_status','suggested_correct_id','suggested_correct_index','alternative_ids','reason','evidence_quotes','question_revision_needed','revision_reason','suggested_text_revision')
        $auditSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $auditValidated = [Collections.Generic.List[object]]::new()
        for ($auditReviewIndex = 0; $auditReviewIndex -lt $auditReviews.GetArrayLength() -and $auditReviewIndex -lt 32; $auditReviewIndex++) {
            Assert-AuditBudget
            $auditReview = $auditReviews[$auditReviewIndex]
            if ($auditReview.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw 'Every review must be a JSON object.' }
            $auditFields = [Collections.Generic.Dictionary[string,System.Text.Json.JsonElement]]::new([StringComparer]::Ordinal)
            $auditFieldCount = 0
            foreach ($auditProperty in $auditReview.EnumerateObject()) {
                Assert-AuditBudget
                $auditFieldCount++
                if ($auditFieldCount -gt 12 -or -not ($auditRequired -ccontains $auditProperty.Name) -or -not $auditFields.TryAdd($auditProperty.Name, $auditProperty.Value)) {
                    throw 'Review duplicate/unknown properties or property count are invalid.'
                }
            }
            if ($auditFieldCount -ne 12) { throw 'Every review requires exactly the 12 declared properties.' }
            $auditRecordId = Require-AuditString $auditFields['record_id'] 'record_id' $true
            $auditQuestionId = Require-AuditString $auditFields['question_id'] 'question_id' $true
            if (-not $auditSources.ContainsKey($auditRecordId) -or -not $auditSeen.Add($auditRecordId)) { throw 'Review record_id is unknown or duplicated.' }
            $auditSource = $auditSources[$auditRecordId]
            $auditQuestion = $auditSource.request.questions[0]
            if ($auditQuestionId -cne $auditQuestion.id) { throw 'Review question_id does not match its original record.' }
            $auditClarity = Require-AuditEnum $auditFields['clarity_status'] 'clarity_status' @('clear','ambiguous','contradictory')
            $auditAnswer = Require-AuditEnum $auditFields['answer_status'] 'answer_status' @('determinate','ambiguous','insufficient_info','invalid_question')
            $auditReason = Require-AuditString $auditFields['reason'] 'reason' $true
            $auditRevisionReason = Require-AuditString $auditFields['revision_reason'] 'revision_reason' $false
            if ($auditFields['question_revision_needed'].ValueKind -notin @([System.Text.Json.JsonValueKind]::True,[System.Text.Json.JsonValueKind]::False)) { throw 'question_revision_needed must be a JSON boolean.' }
            $auditNeedsRevision = $auditFields['question_revision_needed'].GetBoolean()
            $auditRevisionText = $null
            if ($auditNeedsRevision) {
                if ([string]::IsNullOrWhiteSpace($auditRevisionReason)) { throw 'A revision requires a nonempty revision_reason.' }
                $auditRevisionText = Require-AuditString $auditFields['suggested_text_revision'] 'suggested_text_revision' $true
            } elseif ($auditRevisionReason.Length -ne 0 -or $auditFields['suggested_text_revision'].ValueKind -ne [System.Text.Json.JsonValueKind]::Null) {
                throw 'Without revision, revision_reason must be empty and suggested_text_revision must be JSON null.'
            }
            $auditCandidateIds = [Collections.Generic.List[string]]::new()
            $auditCorpus = [Collections.Generic.List[string]]::new()
            Add-AuditCorpus $auditCorpus $auditSource.request.state.text
            Add-AuditCorpus $auditCorpus $auditQuestion.instruction
            Add-AuditCorpus $auditCorpus $auditQuestion.statement
            Add-AuditCorpus $auditCorpus $auditQuestion.when_false
            Add-AuditCorpus $auditCorpus $auditQuestion.when_true
            switch -CaseSensitive ($auditQuestion.type) {
                'choice' {
                    $auditCandidates = @($auditQuestion.candidates)
                    if ($auditCandidates.Count -lt 1 -or $auditCandidates.Count -gt 20) { throw 'Original candidates exceed the bound.' }
                    for ($auditCandidateIndex=0; $auditCandidateIndex -lt $auditCandidates.Count -and $auditCandidateIndex -lt 20; $auditCandidateIndex++) {
                        Assert-AuditBudget
                        $auditCandidate = $auditCandidates[$auditCandidateIndex]
                        if ($auditCandidate.id -isnot [string] -or [string]::IsNullOrWhiteSpace($auditCandidate.id) -or $auditCandidateIds.Contains($auditCandidate.id)) { throw 'Original candidate IDs are invalid.' }
                        $auditCandidateIds.Add($auditCandidate.id)
                        Add-AuditCorpus $auditCorpus $auditCandidate.text
                    }
                }
                'score' {
                    $auditLevels = @($auditQuestion.levels)
                    if ($auditLevels.Count -lt 1 -or $auditLevels.Count -gt 20) { throw 'Original levels exceed the bound.' }
                    for ($auditLevelIndex=0; $auditLevelIndex -lt $auditLevels.Count -and $auditLevelIndex -lt 20; $auditLevelIndex++) {
                        Assert-AuditBudget
                        $auditCandidateIds.Add($auditLevelIndex.ToString([Globalization.CultureInfo]::InvariantCulture))
                        Add-AuditCorpus $auditCorpus $auditLevels[$auditLevelIndex]
                    }
                }
                'boolean' { $auditCandidateIds.Add('false'); $auditCandidateIds.Add('true') }
                default { throw 'Original question type is unsupported.' }
            }
            $auditSuggestedIndex = $null
            $auditSuggestedId = $null
            if ($auditAnswer -ceq 'determinate') {
                $auditParsedIndex = 0
                if ($auditFields['suggested_correct_index'].ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or -not $auditFields['suggested_correct_index'].TryGetInt32([ref]$auditParsedIndex)) {
                    throw 'A determinate suggested_correct_index must be an exact JSON Int32 integer.'
                }
                if ($auditParsedIndex -lt 0 -or $auditParsedIndex -ge $auditCandidateIds.Count) { throw 'Suggested index is out of range.' }
                $auditSuggestedId = Require-AuditString $auditFields['suggested_correct_id'] 'suggested_correct_id' $true
                if ($auditSuggestedId -cne $auditCandidateIds[$auditParsedIndex]) { throw 'Suggested ID/index mapping is inconsistent.' }
                $auditSuggestedIndex = $auditParsedIndex
            } elseif ($auditFields['suggested_correct_index'].ValueKind -ne [System.Text.Json.JsonValueKind]::Null -or $auditFields['suggested_correct_id'].ValueKind -ne [System.Text.Json.JsonValueKind]::Null) {
                throw 'A non-determinate answer requires JSON null suggested ID and index.'
            }
            $auditAlternativeElement = $auditFields['alternative_ids']
            if ($auditAlternativeElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or $auditAlternativeElement.GetArrayLength() -gt 20) { throw 'alternative_ids must be an array of at most20 IDs.' }
            $auditAlternatives = [Collections.Generic.List[string]]::new()
            $auditAlternativeSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            for ($auditAlternativeIndex=0; $auditAlternativeIndex -lt $auditAlternativeElement.GetArrayLength() -and $auditAlternativeIndex -lt 20; $auditAlternativeIndex++) {
                Assert-AuditBudget
                $auditAlternativeId = Require-AuditString $auditAlternativeElement[$auditAlternativeIndex] 'alternative ID' $true
                if (-not $auditCandidateIds.Contains($auditAlternativeId) -or -not $auditAlternativeSeen.Add($auditAlternativeId)) { throw 'Alternative ID is unknown or duplicated.' }
                $auditAlternatives.Add($auditAlternativeId)
            }
            $auditQuoteElement = $auditFields['evidence_quotes']
            if ($auditQuoteElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or $auditQuoteElement.GetArrayLength() -gt 4) { throw 'evidence_quotes must be an array of at most4 quotes.' }
            $auditQuotes = [Collections.Generic.List[string]]::new()
            for ($auditQuoteIndex=0; $auditQuoteIndex -lt $auditQuoteElement.GetArrayLength() -and $auditQuoteIndex -lt 4; $auditQuoteIndex++) {
                Assert-AuditBudget
                $auditQuote = Require-AuditString $auditQuoteElement[$auditQuoteIndex] 'evidence quote' $true
                $auditQuoteMatched = $false
                for ($auditCorpusIndex=0; $auditCorpusIndex -lt $auditCorpus.Count -and $auditCorpusIndex -lt 32; $auditCorpusIndex++) {
                    Assert-AuditBudget
                    if ($auditCorpus[$auditCorpusIndex].Contains($auditQuote,[StringComparison]::Ordinal)) { $auditQuoteMatched = $true; break }
                }
                if (-not $auditQuoteMatched) { throw 'Evidence quote must occur within one original field, with Ordinal comparison.' }
                $auditQuotes.Add($auditQuote)
            }
            if ($auditSource.target_index -isnot [int] -and $auditSource.target_index -isnot [long]) { throw 'Original target_index is not an integer.' }
            if ($auditSource.target_index -lt 0 -or $auditSource.target_index -ge $auditCandidateIds.Count) { throw 'Original target_index is out of range.' }
            $auditChanged = $null
            if ($null -ne $auditSuggestedIndex) { $auditChanged = $auditSuggestedIndex -ne $auditSource.target_index }
            $auditValidated.Add([pscustomobject][ordered]@{
                record_id=$auditRecordId;question_id=$auditQuestionId;family_id=$auditSource.family_id;language=$auditSource.language;split=$auditSource.split;type=$auditQuestion.type;
                original_target_index=$auditSource.target_index;original_target_id=$auditCandidateIds[[int]$auditSource.target_index];
                clarity_status=$auditClarity;answer_status=$auditAnswer;suggested_correct_id=$auditSuggestedId;suggested_correct_index=$auditSuggestedIndex;
                alternative_ids=$auditAlternatives.ToArray();reason=$auditReason;evidence_quotes=$auditQuotes.ToArray();question_revision_needed=$auditNeedsRevision;
                revision_reason=$auditRevisionReason;suggested_text_revision=$auditRevisionText;change_original_label=$auditChanged;
                reviewer_kind='ai';human_review='pending';formal=$false;eligible_as_sealed_test=$false
            })
        }
        if ($auditSeen.Count -ne $SelectedRows.Count -or $auditValidated.Count -ne $SelectedRows.Count) { throw 'Review mapping does not cover the full source denominator.' }
        Assert-AuditBudget
        return $auditValidated.ToArray()
    } finally {
        if ($null -ne $auditDocument) { $auditDocument.Dispose() }
        $auditWatch.Stop()
    }
}
