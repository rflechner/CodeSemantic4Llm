Feature: Select code using abbreviated qualified names
  As a coding agent
  I want to address a method by its declaring type and name
  So that I can omit namespaces when the source address is unambiguous

  Background:
    Given a Git checkout configured for persistent indexing
    And indexing source "CliApplication.cs" is:
      """
      using System.Threading.Tasks;
      namespace TinyHarness.Cli;
      public class CliApplication
      {
          public Task<int> RunAsync() => Task.FromResult(1);
          public Task<int> InvokeAsync() => RunAsync();
      }
      public class OtherCliApplication
      {
          public Task<int> RunAsync() => Task.FromResult(2);
      }
      """
    And indexing source "Other.cs" is:
      """
      namespace Elsewhere;
      public class CliApplication { public int RunAsync() => 3; }
      """

  Scenario: Read, validate and edit a uniquely qualified method without an index
    When default MCP reads, validates and edits "CliApplication.RunAsync" in "CliApplication.cs"
    Then only the requested qualified method body is changed
    And code tool replies contain no indexing details
    And no index database was created

  Scenario: Navigation accepts the same abbreviated qualification
    Given the checkout has a completed index
    When default MCP navigates the abbreviated CLI type and method names
    Then the selected declarations belong to the requested CLI type
    And code tool replies contain no indexing details

  Scenario Outline: A qualification cannot be silently discarded or approximately matched
    Given the checkout has a completed index
    When default MCP selects the CLI method as "<name>"
    Then method operations and navigation reject the name with "No matching"
    And the qualified selection leaves the source unchanged

    Examples:
      | name                         |
      | Application.RunAsync         |
      | Wrong.CliApplication.RunAsync |
      | cliApplication.RunAsync       |
      | CliApplication.Run%           |
      | CliApplication.RunAsyn_       |

  Scenario Outline: Abbreviated names must identify a single method
    Given the CLI source has "<collision>" for the abbreviated method name
    And the checkout has a completed index
    When default MCP selects the CLI method as "CliApplication.RunAsync"
    Then method operations and navigation reject the name with "Several"
    And the qualified selection leaves the source unchanged
    When default MCP selects the second CLI method by its identifier position
    Then only the second CLI method is changed

    Examples:
      | collision                     |
      | overloaded methods            |
      | types in different namespaces |
      | global and namespaced types   |

  Scenario: A nested declaring type can omit its outer type and namespace
    Given the CLI source has a nested application type
    When default MCP reads, validates and edits "CliApplication.RunAsync" in "CliApplication.cs"
    Then only the requested qualified method body is changed
    And no index database was created
