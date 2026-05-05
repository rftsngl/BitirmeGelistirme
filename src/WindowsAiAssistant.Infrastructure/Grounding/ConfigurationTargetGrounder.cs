using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models;
using WindowsAiAssistant.Contracts.Models.Agent;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;
using WindowsAiAssistant.Infrastructure.Launch;
using WindowsAiAssistant.Infrastructure.Targeting;

namespace WindowsAiAssistant.Infrastructure.Grounding;

public sealed class ConfigurationTargetGrounder : ITargetGrounder
{
    private readonly HashSet<string> _allowedExecutablePaths;
    private readonly ApplicationTargetCatalog _applicationCatalog;
    private readonly ILaunchTargetResolver _launchTargetResolver;

    public ConfigurationTargetGrounder(
        ExecutionPolicySettings settings,
        ILaunchTargetResolver launchTargetResolver)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _allowedExecutablePaths = (settings.AllowedExecutablePaths ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizePath)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _applicationCatalog = new ApplicationTargetCatalog(settings);
        _launchTargetResolver = launchTargetResolver ?? throw new ArgumentNullException(nameof(launchTargetResolver));
    }

    public Task<TargetGroundingResult> GroundAsync(
        CommandRequest request,
        IReadOnlyList<TargetReference> resolvedTargets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rawInput = request.UserInput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return Task.FromResult(new TargetGroundingResult
            {
                Disposition = TargetGroundingDisposition.Unresolved,
                InputSource = TargetGroundingInputSource.RawInputFallback,
                ExecutionSuitability = TargetExecutionSuitability.NotExecutable,
                Target = new GroundedTarget
                {
                    Kind = GroundedTargetKind.Unknown,
                    OriginalText = string.Empty,
                    CanonicalValue = string.Empty,
                    Confidence = 0.0
                },
                Reason = TargetGroundingReason.EmptyInput,
                Message = "No target text was provided for grounding."
            });
        }

        var resolvedCandidateSelection = SelectResolvedCandidate(resolvedTargets);
        if (resolvedCandidateSelection.IsAmbiguous)
        {
            return Task.FromResult(CreateAmbiguousApplicationResult(
                resolvedCandidateSelection.OriginalText,
                TargetGroundingInputSource.ResolvedTargetCandidate,
                resolvedCandidateSelection.Confidence));
        }

        if (!string.IsNullOrWhiteSpace(resolvedCandidateSelection.CandidateText))
        {
            return Task.FromResult(GroundCandidate(
                resolvedCandidateSelection.CandidateText,
                resolvedTargets,
                TargetGroundingInputSource.ResolvedTargetCandidate));
        }

        var directRawCandidate = SelectDirectRawCandidate(rawInput);
        if (!string.IsNullOrWhiteSpace(directRawCandidate.ApplicationCanonicalValue))
        {
            return Task.FromResult(CreateResolvedKnownApplicationResult(
                rawInput,
                directRawCandidate.ApplicationCanonicalValue,
                TargetGroundingInputSource.RawInputFallback,
                0.8));
        }

        if (!string.IsNullOrWhiteSpace(directRawCandidate.CandidateText))
        {
            return Task.FromResult(GroundCandidate(
                directRawCandidate.CandidateText,
                [],
                TargetGroundingInputSource.RawInputFallback));
        }

        return Task.FromResult(CreateUnresolvedResult(
            rawInput,
            TargetGroundingInputSource.RawInputFallback,
            TargetGroundingReason.NoKnownApplicationMatch,
            "Target grounding could not resolve a known allowlisted application target."));
    }

    private TargetGroundingResult GroundCandidate(
        string candidateText,
        IReadOnlyList<TargetReference> resolvedTargets,
        TargetGroundingInputSource inputSource)
    {
        var launchTarget = _launchTargetResolver.Resolve(new LaunchTargetReference
        {
            RawReference = candidateText
        });

        if (launchTarget.Kind == LaunchTargetKind.ExecutablePath)
        {
            return GroundExecutablePath(candidateText, launchTarget, inputSource);
        }

        if (launchTarget.Kind == LaunchTargetKind.DocumentPath)
        {
            return GroundDocumentPath(candidateText, launchTarget, inputSource);
        }

        var unsupportedResult = TryCreateUnsupportedResult(candidateText, launchTarget, inputSource);
        if (unsupportedResult is not null)
        {
            return unsupportedResult;
        }

        var candidates = CollectKnownApplicationCandidates(resolvedTargets).ToArray();
        if (candidates.Length > 1)
        {
            return new TargetGroundingResult
            {
                Disposition = TargetGroundingDisposition.Ambiguous,
                InputSource = inputSource,
                ExecutionSuitability = TargetExecutionSuitability.NotExecutable,
                Target = new GroundedTarget
                {
                    Kind = GroundedTargetKind.KnownApplication,
                    OriginalText = candidateText,
                    CanonicalValue = string.Empty,
                    Confidence = candidates.Max(candidate => candidate.Confidence)
                },
                Reason = TargetGroundingReason.MultipleKnownApplicationCandidates,
                Message = "Target grounding found multiple known application candidates and declined execution."
            };
        }

        if (candidates.Length == 1)
        {
            var candidate = candidates[0];
            return new TargetGroundingResult
            {
                Disposition = TargetGroundingDisposition.Resolved,
                InputSource = inputSource,
                ExecutionSuitability = TargetExecutionSuitability.ExecutableHere,
                Target = new GroundedTarget
                {
                    Kind = GroundedTargetKind.KnownApplication,
                    OriginalText = candidateText,
                    CanonicalValue = candidate.CanonicalValue,
                    Confidence = candidate.Confidence
                },
                Reason = TargetGroundingReason.None,
                Message = "Known application target grounded successfully."
            };
        }

        return CreateUnresolvedResult(
            candidateText,
            inputSource,
            TargetGroundingReason.NoKnownApplicationMatch,
            "Target grounding could not resolve a known allowlisted application target.");
    }

    private TargetGroundingResult GroundExecutablePath(
        string candidateText,
        LaunchTarget launchTarget,
        TargetGroundingInputSource inputSource)
    {
        var normalizedPath = NormalizePath(launchTarget.NormalizedReference);

        if (_allowedExecutablePaths.Count == 0)
        {
            return new TargetGroundingResult
            {
                Disposition = TargetGroundingDisposition.Unresolved,
                InputSource = inputSource,
                ExecutionSuitability = TargetExecutionSuitability.NotExecutable,
                Target = new GroundedTarget
                {
                    Kind = GroundedTargetKind.PathLike,
                    OriginalText = candidateText,
                    CanonicalValue = string.Empty,
                    Confidence = 0.9
                },
                Reason = TargetGroundingReason.AllowListMissing,
                Message = "Executable path grounding is fail-closed because executable allowlist is empty or missing."
            };
        }

        if (!_allowedExecutablePaths.Contains(normalizedPath))
        {
            return new TargetGroundingResult
            {
                Disposition = TargetGroundingDisposition.Unsupported,
                InputSource = inputSource,
                ExecutionSuitability = TargetExecutionSuitability.UnsupportedForCurrentLauncher,
                Target = new GroundedTarget
                {
                    Kind = GroundedTargetKind.PathLike,
                    OriginalText = candidateText,
                    CanonicalValue = string.Empty,
                    Confidence = 0.9
                },
                Reason = TargetGroundingReason.ExecutablePathNotAllowlisted,
                Message = "Executable path target is recognized but not allowlisted for launch."
            };
        }

        return new TargetGroundingResult
        {
            Disposition = TargetGroundingDisposition.Resolved,
            InputSource = inputSource,
            ExecutionSuitability = TargetExecutionSuitability.ExecutableHere,
            Target = new GroundedTarget
            {
                Kind = GroundedTargetKind.PathLike,
                OriginalText = candidateText,
                CanonicalValue = normalizedPath,
                Confidence = 0.9
            },
            Reason = TargetGroundingReason.None,
            Message = "Allowlisted executable path grounded successfully."
        };
    }

    private ResolvedCandidateSelection SelectResolvedCandidate(IReadOnlyList<TargetReference> resolvedTargets)
    {
        var firstEligibleCandidateText = string.Empty;
        var firstEligibleOriginalText = string.Empty;
        var canonicalApplicationCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var highestConfidence = 0.0;

        foreach (var target in resolvedTargets)
        {
            if (target.Kind is not (TargetKind.Application or TargetKind.Process or TargetKind.File or TargetKind.Path or TargetKind.Url))
            {
                continue;
            }

            if (!TryGetTargetCandidateText(target, out var candidateText))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(firstEligibleCandidateText))
            {
                firstEligibleCandidateText = candidateText;
                firstEligibleOriginalText = string.IsNullOrWhiteSpace(target.OriginalText)
                    ? candidateText
                    : target.OriginalText;
            }

            highestConfidence = Math.Max(highestConfidence, target.Confidence > 0 ? target.Confidence : 0.9);

            if (target.Kind is TargetKind.Application or TargetKind.Process &&
                TryCanonicalizeApplication(candidateText, out var canonicalValue))
            {
                canonicalApplicationCandidates.Add(canonicalValue);
            }
        }

        if (canonicalApplicationCandidates.Count > 1)
        {
            return new ResolvedCandidateSelection
            {
                IsAmbiguous = true,
                OriginalText = firstEligibleOriginalText,
                Confidence = highestConfidence > 0 ? highestConfidence : 0.9
            };
        }

        if (canonicalApplicationCandidates.Count == 1)
        {
            return new ResolvedCandidateSelection
            {
                CandidateText = canonicalApplicationCandidates.Single()
            };
        }

        if (string.IsNullOrWhiteSpace(firstEligibleCandidateText))
        {
            return new ResolvedCandidateSelection();
        }

        return new ResolvedCandidateSelection
        {
            CandidateText = firstEligibleCandidateText
        };
    }

    private DirectRawCandidateSelection SelectDirectRawCandidate(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return new DirectRawCandidateSelection();
        }

        var trimmed = rawInput.Trim();
        var launchTarget = _launchTargetResolver.Resolve(new LaunchTargetReference
        {
            RawReference = trimmed
        });

        if (launchTarget.Kind is LaunchTargetKind.ExecutablePath or LaunchTargetKind.DocumentPath or LaunchTargetKind.Url)
        {
            return new DirectRawCandidateSelection
            {
                CandidateText = trimmed
            };
        }

        if (_applicationCatalog.TryCanonicalizeSingleValue(trimmed, out var canonicalValue))
        {
            return new DirectRawCandidateSelection
            {
                CandidateText = canonicalValue,
                ApplicationCanonicalValue = canonicalValue
            };
        }

        return new DirectRawCandidateSelection();
    }

    private static bool TryGetTargetCandidateText(TargetReference target, out string candidateText)
    {
        candidateText = string.Empty;

        if (!string.IsNullOrWhiteSpace(target.NormalizedValue))
        {
            candidateText = target.NormalizedValue.Trim();
            return true;
        }

        if (!string.IsNullOrWhiteSpace(target.DisplayName))
        {
            candidateText = target.DisplayName.Trim();
            return true;
        }

        if (!string.IsNullOrWhiteSpace(target.OriginalText))
        {
            candidateText = target.OriginalText.Trim();
            return true;
        }

        return false;
    }

    private static TargetGroundingResult CreateUnresolvedResult(
        string originalText,
        TargetGroundingInputSource inputSource,
        TargetGroundingReason reason,
        string message)
    {
        return new TargetGroundingResult
        {
            Disposition = TargetGroundingDisposition.Unresolved,
            InputSource = inputSource,
            ExecutionSuitability = TargetExecutionSuitability.NotExecutable,
            Target = new GroundedTarget
            {
                Kind = GroundedTargetKind.Unknown,
                OriginalText = originalText,
                CanonicalValue = string.Empty,
                Confidence = 0.0
            },
            Reason = reason,
            Message = message
        };
    }

    private static TargetGroundingResult CreateResolvedKnownApplicationResult(
        string originalText,
        string canonicalValue,
        TargetGroundingInputSource inputSource,
        double confidence)
    {
        return new TargetGroundingResult
        {
            Disposition = TargetGroundingDisposition.Resolved,
            InputSource = inputSource,
            ExecutionSuitability = TargetExecutionSuitability.ExecutableHere,
            Target = new GroundedTarget
            {
                Kind = GroundedTargetKind.KnownApplication,
                OriginalText = originalText,
                CanonicalValue = canonicalValue,
                Confidence = confidence
            },
            Reason = TargetGroundingReason.None,
            Message = "Known application target grounded successfully."
        };
    }

    private static TargetGroundingResult CreateResolvedDocumentResult(
        string originalText,
        string canonicalValue,
        TargetGroundingInputSource inputSource,
        double confidence)
    {
        return new TargetGroundingResult
        {
            Disposition = TargetGroundingDisposition.Resolved,
            InputSource = inputSource,
            ExecutionSuitability = TargetExecutionSuitability.ResolvedButNotExecutableHere,
            Target = new GroundedTarget
            {
                Kind = GroundedTargetKind.DocumentLike,
                OriginalText = originalText,
                CanonicalValue = canonicalValue,
                Confidence = confidence
            },
            Reason = TargetGroundingReason.None,
            Message = "Document-like target grounded successfully."
        };
    }

    private static TargetGroundingResult CreateAmbiguousApplicationResult(
        string originalText,
        TargetGroundingInputSource inputSource,
        double confidence)
    {
        return new TargetGroundingResult
        {
            Disposition = TargetGroundingDisposition.Ambiguous,
            InputSource = inputSource,
            ExecutionSuitability = TargetExecutionSuitability.NotExecutable,
            Target = new GroundedTarget
            {
                Kind = GroundedTargetKind.KnownApplication,
                OriginalText = originalText,
                CanonicalValue = string.Empty,
                Confidence = confidence
            },
            Reason = TargetGroundingReason.MultipleKnownApplicationCandidates,
            Message = "Target grounding found multiple known application candidates and declined execution."
        };
    }

    private TargetGroundingResult? TryCreateUnsupportedResult(
        string rawTargetText,
        LaunchTarget launchTarget,
        TargetGroundingInputSource inputSource)
    {
        return launchTarget.Kind switch
        {
            LaunchTargetKind.ExecutablePath => CreateUnsupportedResult(
                rawTargetText,
                inputSource,
                GroundedTargetKind.PathLike,
                TargetGroundingReason.UnsupportedPathLikeTarget,
                "Path-like targets are recognized but not supported by this launch path."),
            LaunchTargetKind.Url => CreateUnsupportedResult(
                rawTargetText,
                inputSource,
                GroundedTargetKind.UrlLike,
                TargetGroundingReason.UnsupportedUrlTarget,
                "URL-like targets are recognized but not supported by this launch path."),
            _ => null
        };
    }

    private TargetGroundingResult CreateUnsupportedResult(
        string rawTargetText,
        TargetGroundingInputSource inputSource,
        GroundedTargetKind kind,
        TargetGroundingReason reason,
        string message)
    {
        return new TargetGroundingResult
        {
            Disposition = TargetGroundingDisposition.Unsupported,
            InputSource = inputSource,
            ExecutionSuitability = TargetExecutionSuitability.UnsupportedForCurrentLauncher,
            Target = new GroundedTarget
            {
                Kind = kind,
                OriginalText = rawTargetText,
                CanonicalValue = string.Empty,
                Confidence = 0.9
            },
            Reason = reason,
            Message = message
        };
    }

    private IEnumerable<(string CanonicalValue, double Confidence)> CollectKnownApplicationCandidates(
        IReadOnlyList<TargetReference> resolvedTargets)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in resolvedTargets
                     .Where(target => target.Kind is TargetKind.Application or TargetKind.Process))
        {
            if (TryCanonicalizeApplication(candidate.NormalizedValue, out var canonicalValue) ||
                TryCanonicalizeApplication(candidate.DisplayName, out canonicalValue) ||
                TryCanonicalizeApplication(candidate.OriginalText, out canonicalValue))
            {
                if (seen.Add(canonicalValue))
                {
                    yield return (canonicalValue, candidate.Confidence > 0 ? candidate.Confidence : 0.9);
                }
            }
        }
    }

    private bool TryCanonicalizeApplication(string? value, out string canonicalValue)
    {
        return _applicationCatalog.TryCanonicalize(value, out canonicalValue);
    }

    private TargetGroundingResult GroundDocumentPath(
        string candidateText,
        LaunchTarget launchTarget,
        TargetGroundingInputSource inputSource)
    {
        var normalizedPath = NormalizePath(launchTarget.NormalizedReference);
        return CreateResolvedDocumentResult(
            candidateText,
            normalizedPath,
            inputSource,
            0.9);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim().Trim('"', '\'');
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private sealed class ResolvedCandidateSelection
    {
        public string CandidateText { get; init; } = string.Empty;
        public string OriginalText { get; init; } = string.Empty;
        public double Confidence { get; init; }
        public bool IsAmbiguous { get; init; }
    }

    private sealed class DirectRawCandidateSelection
    {
        public string CandidateText { get; init; } = string.Empty;
        public string ApplicationCanonicalValue { get; init; } = string.Empty;
    }
}
