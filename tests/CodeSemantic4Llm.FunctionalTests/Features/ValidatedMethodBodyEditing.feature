Feature: Validate and replace methods in a single request
  As a coding agent
  I want an edit to return precise syntax errors without changing invalid source
  So that I can correct and retry the same tool without a separate validation call

  Background:
    Given a Git checkout configured for persistent indexing
    And indexing source "Worker.cs" is:
      """
      class Worker
      {
          public int Run() => 1;
          public int Keep() => 7;
      }
      """

  Scenario Outline: One edit request reports syntax errors and a corrected retry succeeds
    When "<mode>" MCP directly replaces Run with a missing semicolon
    Then the edit reports CS1002 at proposed file position 4:18 without writing
    When the same MCP client retries with a corrected body
    Then only the Run body is replaced
    And code tool replies contain no indexing details
    And no index database was created

    Examples:
      | mode     |
      | default  |
      | indexing |

  Scenario: Large syntax failures produce bounded feedback
    When MCP directly replaces Run with 15 syntax errors
    Then the edit reports 10 errors and indicates 5 additional errors
    And the rejected default MCP write leaves the source unchanged
    And no index database was created

  Scenario: A complete declaration receives body-specific guidance without a validation call
    When MCP directly supplies a complete declaration as the Run body
    Then the edit explains that only a body can be replaced
    And the rejected default MCP write leaves the source unchanged

  Scenario Outline: A full method with an unterminated string returns compiler syntax diagnostics
    When "<mode>" MCP directly replaces Run with this declaration:
      """
      public int Run()
      {
          errors.MarkupLine("[red]tinyharness:[/] \\u0022 Markup.Escape(exception.Message));
          return 42;
      }
      """
    Then the declaration edit reports CS1010 at proposed file position 5:27 without writing
    When the same MCP client fixes the string and retries the declaration edit
    Then the corrected declaration is written and the other method is preserved
    And code tool replies contain no indexing details
    And no index database was created

    Examples:
      | mode     |
      | default  |
      | indexing |

  Scenario: Large declaration failures produce bounded feedback
    When MCP directly replaces the Run declaration with 15 syntax errors
    Then the edit reports 10 errors and indicates 5 additional errors
    And the rejected default MCP write leaves the source unchanged

  Scenario Outline: A multi-character literal receives precise quoting guidance
    When "<mode>" MCP attempts "<tool>" with a multi-character literal
    Then the edit explains CS1012 with one concise C# quoting hint
    And the rejected default MCP write leaves the source unchanged
    When the same MCP client corrects the string quotes and retries
    Then the corrected string and valid character literal are preserved
    And code tool replies contain no indexing details

    Examples:
      | mode     | tool                |
      | default  | replace_method_body |
      | default  | replace_method      |
      | indexing | replace_method_body |
      | indexing | replace_method      |
