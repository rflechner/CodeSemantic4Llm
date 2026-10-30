Feature: Keep source text readable in MCP responses
  As a coding agent
  I want quotes and operators to remain recognizable in returned code
  So that JSON encoding does not introduce Unicode escape sequences that I might copy into C#

  Background:
    Given a Git checkout configured for persistent indexing
    And indexing source "Worker.cs" is:
      """
      class Worker
      {
          public int Run(Exception exception)
          {
              // Preserve été, l'apostrophe, <tags> and &.
              var values = new List<string>();
              var literal = @"\u0022\u002B";
              var quoted = "\"quoted\"";
              errors.MarkupLine("[red]tinyharness:[/] " + Markup.Escape(exception.Message));
              return 1;
          }
      }
      """

  Scenario Outline: Read and edit source without adding or decoding C# escapes
    When "<mode>" MCP reads Run using "<reader>"
    Then the model-facing JSON keeps C# punctuation and Unicode readable
    When MCP writes the returned code with only the return value changed
    Then source encoding and literal escape sequences are preserved
    And no index database was created

    Examples:
      | mode     | reader          |
      | default  | get_method      |
      | default  | get_method_body |
      | indexing | get_method      |
      | indexing | get_method_body |
