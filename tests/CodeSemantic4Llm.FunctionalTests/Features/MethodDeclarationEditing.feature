Feature: Replace a complete C# method when its signature changes
  As a coding agent
  I want separate tools for body edits and complete declarations
  So that signature changes are explicit and confined to the selected method

  Background:
    Given a Git checkout configured for persistent indexing
    And indexing source "Worker.cs" is:
      """
      class Worker
      {
          // Keep this comment.
          [Old]
          public int Run(int value) => value;
          public int Run() => 7;
          public int Call() => Run(1);
      }
      """

  Scenario Outline: Change one overload's attributes, name, return type and parameters
    When "<mode>" MCP reads the first Run declaration in pages and replaces it with:
      """
      [New]
      [Other]
      public string Execute(string text)
      {
          return text;
      }
      """
    Then source file "Worker.cs" is exactly:
      """
      class Worker
      {
          // Keep this comment.
          [New]
          [Other]
          public string Execute(string text)
          {
              return text;
          }
          public int Run() => 7;
          public int Call() => Run(1);
      }
      """
    And the declaration edit returns the new identifier position for "Execute"

    Examples:
      | mode     |
      | default  |
      | indexing |

  Scenario Outline: A full declaration cannot be accidentally inserted as a method body
    When default MCP tries to replace the first Run body with "<declaration>"
    Then the declaration request fails with "replace_method"
    And the rejected default MCP write leaves the source unchanged

    Examples:
      | declaration                          |
      | public int Run() { return 42; }      |
      | int Run() { return 42; }             |

  Scenario Outline: Full method edits use current source without requiring a previous read
    When "<mode>" MCP replaces the first Run declaration after "<change>"
    Then the declaration edit returns the new identifier position for "Execute"
    And code tool replies contain no indexing details
    And no index database was created

    Examples:
      | mode     | change         |
      | default  | no read        |
      | default  | body read      |
      | default  | source edit    |
      | default  | branch switch  |
      | default  | server restart |
      | indexing | no read        |
      | indexing | server restart |

  Scenario: Invalid whole-method replacement leaves the source unchanged
    When default MCP tries to replace a method with multiple declarations without reading
    Then the declaration request fails with "exactly one"
    And the rejected default MCP write leaves the source unchanged

  Scenario: A complete-method continuation cannot be used to read body pages
    When default MCP uses a complete-method continuation to read a body
    Then the declaration request fails with "Start again at offset 0"

  Scenario: A changed source invalidates complete-method pagination
    When default MCP continues a complete-method read after changing its source
    Then the declaration request fails with "source changed"
